namespace AssetStudio.AppCore.Decompilation;

/// <summary>
/// Service for resolving assemblies and decompiling types into C# source code.
/// </summary>
public interface ICSharpDecompilerService : IDisposable
{
    /// <summary>
    /// Gets or sets the primary assembly / Managed directory.
    /// </summary>
    string? AssemblyDirectory { get; set; }

    /// <summary>
    /// Registers an additional directory to probe for assemblies.
    /// </summary>
    void RegisterProbePath(string path);

    /// <summary>
    /// Attempts to locate the full path for a given assembly name.
    /// </summary>
    bool TryFindAssembly(string assemblyName, out string fullPath);

    /// <summary>
    /// Decompiles the specified type from the given assembly into C# source code.
    /// Returns null if the assembly or type could not be resolved.
    /// </summary>
    Task<string?> DecompileTypeAsync(
        string assemblyName,
        string className,
        string? @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears any cached decompiled code or module definitions.
    /// </summary>
    void ClearCache();
}
