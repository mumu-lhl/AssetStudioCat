using System.Text.Json;

namespace AssetStudio.AppCore.Caching;

public sealed class CacheCatalog
{
    private readonly string _indexesRoot;

    public CacheCatalog(CacheLayout layout)
    {
        _indexesRoot = Path.GetFullPath(layout.Indexes);
    }

    public async Task<IReadOnlyList<CacheIndexInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_indexesRoot))
        {
            return [];
        }

        var results = new List<CacheIndexInfo>();
        foreach (var directory in Directory.EnumerateDirectories(_indexesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                var fingerprint = root.GetProperty("fingerprint");
                results.Add(new CacheIndexInfo(
                    Path.GetFullPath(directory),
                    fingerprint.GetProperty("rootPath").GetString() ?? "Unknown source",
                    fingerprint.GetProperty("fileCount").GetInt32(),
                    fingerprint.GetProperty("totalBytes").GetInt64(),
                    root.GetProperty("entryCount").GetInt64(),
                    GetDirectorySize(directory, cancellationToken),
                    root.GetProperty("createdAt").GetDateTimeOffset()));
            }
            catch (JsonException)
            {
                // Ignore incomplete or obsolete cache entries; rebuild will replace them.
            }
            catch (IOException)
            {
                // An index may be replaced while the catalog is being read.
            }
        }
        return results.OrderByDescending(result => result.CreatedAt).ToArray();
    }

    public void Delete(CacheIndexInfo entry)
    {
        var path = ValidateChild(entry.DirectoryPath);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    public void Clear()
    {
        if (!Directory.Exists(_indexesRoot))
        {
            return;
        }
        foreach (var directory in Directory.EnumerateDirectories(_indexesRoot))
        {
            Directory.Delete(ValidateChild(directory), true);
        }
    }

    private string ValidateChild(string candidate)
    {
        var fullPath = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(_indexesRoot, fullPath);
        if (relative is "." or ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("The cache entry is outside the configured index root.");
        }
        return fullPath;
    }

    private static long GetDirectorySize(string directory, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += new FileInfo(file).Length;
        }
        return total;
    }
}
