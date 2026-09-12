using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AssetStudio.AppCore.Indexing;

public sealed class DiskAssetIndex : IAssetIndex
{
    private const int SchemaVersion = 4;
    private const string MetadataFileName = "metadata.json";
    private const string RowsFileName = "assets.jsonl";
    private const string OffsetsFileName = "assets.offsets";
    private readonly string _indexRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DiskAssetIndex(string indexesRoot, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var canonicalSource = Path.GetFullPath(sourcePath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSource)))[..24];
        _indexRoot = Path.Combine(Path.GetFullPath(indexesRoot), key);
    }

    public string DirectoryPath => _indexRoot;

    public async Task BuildAsync(
        AssetSourceFingerprint fingerprint,
        IAsyncEnumerable<AssetIndexEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(_indexRoot)!;
        System.IO.Directory.CreateDirectory(parent);
        var staging = _indexRoot + ".building-" + Guid.NewGuid().ToString("N");
        System.IO.Directory.CreateDirectory(staging);

        try
        {
            var rowsPath = Path.Combine(staging, RowsFileName);
            var offsetsPath = Path.Combine(staging, OffsetsFileName);
            long count = 0;
            await using (var rows = new FileStream(rowsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await using (var offsets = new FileStream(offsetsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, true))
            {
                await foreach (var entry in entries.WithCancellation(cancellationToken))
                {
                    var offsetBytes = BitConverter.GetBytes(rows.Position);
                    await offsets.WriteAsync(offsetBytes, cancellationToken);
                    await JsonSerializer.SerializeAsync(rows, entry, _jsonOptions, cancellationToken);
                    await rows.WriteAsync("\n"u8.ToArray(), cancellationToken);
                    count++;
                }
            }

            var metadata = new IndexMetadata(SchemaVersion, fingerprint, count, DateTimeOffset.UtcNow);
            await using (var metadataStream = File.Create(Path.Combine(staging, MetadataFileName)))
            {
                await JsonSerializer.SerializeAsync(metadataStream, metadata, _jsonOptions, cancellationToken);
            }

            Promote(staging);
        }
        catch
        {
            if (System.IO.Directory.Exists(staging))
            {
                System.IO.Directory.Delete(staging, true);
            }
            throw;
        }
    }

    public async Task<bool> IsCurrentAsync(AssetSourceFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_indexRoot, MetadataFileName));
            var metadata = await JsonSerializer.DeserializeAsync<IndexMetadata>(stream, _jsonOptions, cancellationToken);
            return metadata is { SchemaVersion: SchemaVersion } && metadata.Fingerprint == fingerprint
                && File.Exists(Path.Combine(_indexRoot, RowsFileName))
                && File.Exists(Path.Combine(_indexRoot, OffsetsFileName));
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<AssetIndexPage> QueryAsync(AssetIndexQuery query, CancellationToken cancellationToken = default)
    {
        query = query.Normalize();
        var metadata = await ReadMetadataAsync(cancellationToken);
        return query.SearchText is null && query.TypeName is null
            ? await ReadDirectPageAsync(query, metadata.EntryCount, cancellationToken)
            : await ReadFilteredPageAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ResolveObjectSourcesAsync(
        IEnumerable<string> serializedFileNames,
        CancellationToken cancellationToken = default)
    {
        var names = serializedFileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return [];
        }

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(Path.Combine(_indexRoot, RowsFileName), Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var entry = JsonSerializer.Deserialize<AssetIndexEntry>(line, _jsonOptions)!;
            if (names.Contains(Path.GetFileName(entry.SerializedFile)))
            {
                sources.Add(entry.ObjectSourcePath);
            }
        }
        return sources.ToArray();
    }

    public async IAsyncEnumerable<AssetIndexEntry> EnumerateAsync(
        string? searchText = null,
        string? typeName = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = new AssetIndexQuery(SearchText: searchText, TypeName: typeName).Normalize();
        using var reader = new StreamReader(Path.Combine(_indexRoot, RowsFileName), Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var entry = JsonSerializer.Deserialize<AssetIndexEntry>(line, _jsonOptions)!;
            if (Matches(entry, query))
            {
                yield return entry;
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, long>> GetTypeCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await foreach (var entry in EnumerateAsync(cancellationToken: cancellationToken))
        {
            counts.TryGetValue(entry.TypeName, out var count);
            counts[entry.TypeName] = count + 1;
        }
        return counts;
    }

    public void Rebuild()
    {
        if (System.IO.Directory.Exists(_indexRoot))
        {
            System.IO.Directory.Delete(_indexRoot, true);
        }
    }

    private async Task<AssetIndexPage> ReadDirectPageAsync(AssetIndexQuery query, long total, CancellationToken cancellationToken)
    {
        if (query.Offset >= total)
        {
            return new AssetIndexPage([], query.Offset, null, total);
        }

        await using var offsets = File.OpenRead(Path.Combine(_indexRoot, OffsetsFileName));
        offsets.Position = query.Offset * sizeof(long);
        var bytes = new byte[sizeof(long)];
        await offsets.ReadExactlyAsync(bytes, cancellationToken);
        var rowOffset = BitConverter.ToInt64(bytes);

        using var rows = new FileStream(Path.Combine(_indexRoot, RowsFileName), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        rows.Position = rowOffset;
        using var reader = new StreamReader(rows, Encoding.UTF8, false, 64 * 1024, leaveOpen: false);
        var items = new List<AssetIndexEntry>(query.Limit);
        while (items.Count < query.Limit && await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            items.Add(JsonSerializer.Deserialize<AssetIndexEntry>(line, _jsonOptions)!);
        }

        int? next = query.Offset + items.Count < total ? query.Offset + items.Count : null;
        return new AssetIndexPage(items, query.Offset, next, total);
    }

    private async Task<AssetIndexPage> ReadFilteredPageAsync(AssetIndexQuery query, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Path.Combine(_indexRoot, RowsFileName), Encoding.UTF8);
        var items = new List<AssetIndexEntry>(query.Limit);
        var matched = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var entry = JsonSerializer.Deserialize<AssetIndexEntry>(line, _jsonOptions)!;
            if (!Matches(entry, query))
            {
                continue;
            }
            if (matched++ < query.Offset)
            {
                continue;
            }
            if (items.Count < query.Limit)
            {
                items.Add(entry);
                continue;
            }
            return new AssetIndexPage(items, query.Offset, query.Offset + items.Count, null);
        }
        return new AssetIndexPage(items, query.Offset, null, query.Offset + items.Count);
    }

    private static bool Matches(AssetIndexEntry entry, AssetIndexQuery query)
    {
        if (query.TypeName is not null && !entry.TypeName.Equals(query.TypeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return query.SearchText is null
            || entry.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)
            || (entry.Container?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task<IndexMetadata> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.Combine(_indexRoot, MetadataFileName));
        return await JsonSerializer.DeserializeAsync<IndexMetadata>(stream, _jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Asset index metadata is empty.");
    }

    private void Promote(string staging)
    {
        var previous = _indexRoot + ".previous";
        if (System.IO.Directory.Exists(previous))
        {
            System.IO.Directory.Delete(previous, true);
        }
        if (System.IO.Directory.Exists(_indexRoot))
        {
            System.IO.Directory.Move(_indexRoot, previous);
        }
        try
        {
            System.IO.Directory.Move(staging, _indexRoot);
            if (System.IO.Directory.Exists(previous))
            {
                System.IO.Directory.Delete(previous, true);
            }
        }
        catch
        {
            if (!System.IO.Directory.Exists(_indexRoot) && System.IO.Directory.Exists(previous))
            {
                System.IO.Directory.Move(previous, _indexRoot);
            }
            throw;
        }
    }

    private sealed record IndexMetadata(
        int SchemaVersion,
        AssetSourceFingerprint Fingerprint,
        long EntryCount,
        DateTimeOffset CreatedAt);
}
