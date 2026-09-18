using System.Collections.Concurrent;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace AssetStudio.AppCore.Decompilation;

/// <summary>
/// Default implementation of <see cref="ICSharpDecompilerService"/> using ICSharpCode.Decompiler.
/// </summary>
public sealed class CSharpDecompilerService : ICSharpDecompilerService
{
    private readonly HashSet<string> _probeDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _decompiledCache = new();
    private readonly object _lock = new();
    private string? _assemblyDirectory;

    public string? AssemblyDirectory
    {
        get => _assemblyDirectory;
        set
        {
            lock (_lock)
            {
                if (!string.Equals(_assemblyDirectory, value, StringComparison.OrdinalIgnoreCase))
                {
                    _assemblyDirectory = value;
                    if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    {
                        _probeDirectories.Add(Path.GetFullPath(value));
                    }
                    ClearCache();
                }
            }
        }
    }

    public void RegisterProbePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string? dir = null;
            if (Directory.Exists(path))
            {
                dir = Path.GetFullPath(path);
            }
            else if (File.Exists(path))
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(path));
            }

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                lock (_lock)
                {
                    _probeDirectories.Add(dir);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    public bool TryFindAssembly(string assemblyName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        var candidateName = assemblyName;
        if (!candidateName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            candidateName += ".dll";
        }

        List<string> directories;
        lock (_lock)
        {
            directories = [.. _probeDirectories];
            if (!string.IsNullOrWhiteSpace(_assemblyDirectory) && !_probeDirectories.Contains(_assemblyDirectory))
            {
                directories.Insert(0, _assemblyDirectory);
            }
        }

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            // Direct check
            var direct = Path.Combine(dir, candidateName);
            if (File.Exists(direct))
            {
                fullPath = Path.GetFullPath(direct);
                return true;
            }

            // Case-insensitive file search in directory
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(Path.GetFileName(file), candidateName, StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath = Path.GetFullPath(file);
                        return true;
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        return false;
    }

    public async Task<string?> DecompileTypeAsync(
        string assemblyName,
        string className,
        string? @namespace,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        if (!TryFindAssembly(assemblyName, out var assemblyPath))
        {
            return null;
        }

        var cleanNamespace = string.IsNullOrWhiteSpace(@namespace) ? string.Empty : @namespace.Trim();
        var cleanClass = className.Trim();
        var cacheKey = $"{assemblyPath}::{cleanNamespace}::{cleanClass}";

        if (_decompiledCache.TryGetValue(cacheKey, out var cachedCode))
        {
            return cachedCode;
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var searchDirs = GetActiveDirectories(Path.GetDirectoryName(assemblyPath));
            var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, targetFramework: null);
            foreach (var dir in searchDirs)
            {
                resolver.AddSearchDirectory(dir);
            }

            var settings = new DecompilerSettings
            {
                ThrowOnAssemblyResolveErrors = false,
                ShowXmlDocumentation = true,
            };

            var decompiler = new CSharpDecompiler(assemblyPath, resolver, settings);
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Try fully qualified name
            var fullTypeNameString = string.IsNullOrEmpty(cleanNamespace)
                ? cleanClass
                : $"{cleanNamespace}.{cleanClass}";

            var fullTypeName = new FullTypeName(fullTypeNameString);
            var typeDef = decompiler.TypeSystem.FindType(fullTypeName).GetDefinition();

            // 2. If not found, try nested type syntax (replace . with +)
            if (typeDef is null && cleanClass.Contains('.'))
            {
                var nestedSyntax = cleanClass.Replace('.', '+');
                var nestedFullName = string.IsNullOrEmpty(cleanNamespace)
                    ? nestedSyntax
                    : $"{cleanNamespace}.{nestedSyntax}";
                typeDef = decompiler.TypeSystem.FindType(new FullTypeName(nestedFullName)).GetDefinition();
            }

            // 3. Fallback: Search all types in the module
            if (typeDef is null)
            {
                var candidates = decompiler.TypeSystem.MainModule.TypeDefinitions;
                foreach (var candidate in candidates)
                {
                    if (string.Equals(candidate.Name, cleanClass, StringComparison.Ordinal) ||
                        string.Equals(candidate.ReflectionName, cleanClass, StringComparison.Ordinal) ||
                        string.Equals(candidate.FullName, fullTypeNameString, StringComparison.Ordinal))
                    {
                        typeDef = candidate;
                        break;
                    }
                }
            }

            if (typeDef is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var decompiled = decompiler.DecompileTypeAsString(typeDef.FullTypeName);
            if (!string.IsNullOrEmpty(decompiled))
            {
                _decompiledCache.TryAdd(cacheKey, decompiled);
            }

            return decompiled;
        }, cancellationToken);
    }

    public void ClearCache()
    {
        _decompiledCache.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        lock (_lock)
        {
            _probeDirectories.Clear();
        }
    }

    private List<string> GetActiveDirectories(string? assemblyDir)
    {
        lock (_lock)
        {
            var dirs = new HashSet<string>(_probeDirectories, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(_assemblyDirectory))
            {
                dirs.Add(_assemblyDirectory);
            }
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                dirs.Add(assemblyDir);
            }
            return [.. dirs];
        }
    }
}
