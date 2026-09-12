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
    private volatile AssetIndexEntry[]? _cachedEntries;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

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
            var cached = new List<AssetIndexEntry>();
            await using (var rows = new FileStream(rowsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await using (var offsets = new FileStream(offsetsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, true))
            {
                await foreach (var entry in entries.WithCancellation(cancellationToken))
                {
                    cached.Add(entry);
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
            _cachedEntries = cached.ToArray();
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
        var allEntries = await EnsureLoadedAsync(cancellationToken);

        var typeName = query.TypeName;
        var containerPath = query.ContainerPath;
        var searchText = query.SearchText;
        var isNoContainer = containerPath == "(No Container)";
        var normQuery = containerPath is not null && !isNoContainer
            ? containerPath.Replace('\\', '/').Trim('/')
            : null;
        var normQuerySlash = normQuery is not null ? normQuery + "/" : null;
        var exactContainer = query.ExactContainer;

        bool Filter(AssetIndexEntry entry)
        {
            if (typeName is not null && !entry.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (containerPath is not null)
            {
                if (isNoContainer)
                {
                    if (!string.IsNullOrEmpty(entry.Container))
                    {
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(entry.Container))
                    {
                        return false;
                    }
                    var normEntry = entry.Container.Replace('\\', '/').Trim('/');
                    if (exactContainer)
                    {
                        if (!normEntry.Equals(normQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!normEntry.Equals(normQuery, StringComparison.OrdinalIgnoreCase) &&
                            !normEntry.StartsWith(normQuerySlash!, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
            }
            if (searchText is not null)
            {
                if (!entry.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
                    !(entry.Container?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return false;
                }
            }
            return true;
        }

        var hasFilter = typeName is not null || containerPath is not null || searchText is not null;

        if (query.SortField == AssetSortField.IndexOrder)
        {
            if (!hasFilter)
            {
                var total = allEntries.Length;
                if (query.Offset >= total)
                {
                    return new AssetIndexPage([], query.Offset, null, total);
                }
                var count = Math.Min(query.Limit, total - query.Offset);
                var items = new AssetIndexEntry[count];
                Array.Copy(allEntries, query.Offset, items, 0, count);
                int? next = query.Offset + count < total ? query.Offset + count : null;
                return new AssetIndexPage(items, query.Offset, next, total);
            }
            else
            {
                var items = new List<AssetIndexEntry>(Math.Min(query.Limit, 250));
                var matched = 0;
                for (var i = 0; i < allEntries.Length; i++)
                {
                    var entry = allEntries[i];
                    if (!Filter(entry)) continue;

                    if (matched >= query.Offset && items.Count < query.Limit)
                    {
                        items.Add(entry);
                    }
                    matched++;
                }
                int? next = query.Offset + items.Count < matched ? query.Offset + items.Count : null;
                return new AssetIndexPage(items, query.Offset, next, matched);
            }
        }
        else
        {
            var matchedList = new List<AssetIndexEntry>();
            for (var i = 0; i < allEntries.Length; i++)
            {
                var entry = allEntries[i];
                if (Filter(entry))
                {
                    matchedList.Add(entry);
                }
            }
            var comparer = new AssetEntryComparer(query.SortField, query.SortDescending);
            matchedList.Sort(comparer);

            var total = matchedList.Count;
            var paged = matchedList.Skip(query.Offset).Take(query.Limit).ToArray();
            int? next = query.Offset + paged.Length < total ? query.Offset + paged.Length : null;
            return new AssetIndexPage(paged, query.Offset, next, total);
        }
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

        var allEntries = await EnsureLoadedAsync(cancellationToken);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allEntries.Length; i++)
        {
            var entry = allEntries[i];
            if (names.Contains(Path.GetFileName(entry.SerializedFile)))
            {
                sources.Add(entry.ObjectSourcePath);
            }
        }
        return sources.ToArray();
    }

    public IAsyncEnumerable<AssetIndexEntry> EnumerateAsync(
        string? searchText = null,
        string? typeName = null,
        CancellationToken cancellationToken = default) =>
        EnumerateAsync(searchText, typeName, null, false, cancellationToken);

    public async IAsyncEnumerable<AssetIndexEntry> EnumerateAsync(
        string? searchText,
        string? typeName,
        string? containerPath,
        bool exactContainer = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var allEntries = await EnsureLoadedAsync(cancellationToken);
        var query = new AssetIndexQuery(
            SearchText: searchText,
            TypeName: typeName,
            ContainerPath: containerPath,
            ExactContainer: exactContainer).Normalize();

        for (var i = 0; i < allEntries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = allEntries[i];
            if (Matches(entry, query))
            {
                yield return entry;
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, long>> GetTypeCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var allEntries = await EnsureLoadedAsync(cancellationToken);
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allEntries.Length; i++)
        {
            var type = allEntries[i].TypeName;
            counts.TryGetValue(type, out var count);
            counts[type] = count + 1;
        }
        return counts;
    }

    public void Rebuild()
    {
        _cachedEntries = null;
        if (System.IO.Directory.Exists(_indexRoot))
        {
            System.IO.Directory.Delete(_indexRoot, true);
        }
    }

    private async Task<AssetIndexEntry[]> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cachedEntries is { } existing)
        {
            return existing;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedEntries is { } lockedExisting)
            {
                return lockedExisting;
            }

            var rowsPath = Path.Combine(_indexRoot, RowsFileName);
            if (!File.Exists(rowsPath))
            {
                return [];
            }

            var list = new List<AssetIndexEntry>();
            var stringPool = new Dictionary<string, string>(StringComparer.Ordinal);
            string Intern(string s)
            {
                if (stringPool.TryGetValue(s, out var pooled)) return pooled;
                stringPool[s] = s;
                return s;
            }

            using var stream = new FileStream(rowsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 128 * 1024);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var raw = JsonSerializer.Deserialize<AssetIndexEntry>(line, _jsonOptions);
                if (raw is not null)
                {
                    var entry = raw with
                    {
                        TypeName = Intern(raw.TypeName),
                        SourcePath = Intern(raw.SourcePath),
                        ObjectSourcePath = Intern(raw.ObjectSourcePath),
                        SerializedFile = Intern(raw.SerializedFile),
                        GameObjectSerializedFile = raw.GameObjectSerializedFile is not null ? Intern(raw.GameObjectSerializedFile) : null,
                        ParentTransformSerializedFile = raw.ParentTransformSerializedFile is not null ? Intern(raw.ParentTransformSerializedFile) : null
                    };
                    list.Add(entry);
                }
            }

            var result = list.ToArray();
            _cachedEntries = result;
            return result;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static bool Matches(AssetIndexEntry entry, AssetIndexQuery query)
    {
        if (query.TypeName is not null && !entry.TypeName.Equals(query.TypeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (query.ContainerPath is not null)
        {
            if (query.ContainerPath == "(No Container)")
            {
                if (!string.IsNullOrEmpty(entry.Container))
                {
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(entry.Container))
                {
                    return false;
                }
                var normEntry = entry.Container.Replace('\\', '/').Trim('/');
                var normQuery = query.ContainerPath.Replace('\\', '/').Trim('/');
                if (query.ExactContainer)
                {
                    if (!normEntry.Equals(normQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!normEntry.Equals(normQuery, StringComparison.OrdinalIgnoreCase) &&
                        !normEntry.StartsWith(normQuery + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
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

    private sealed class AssetEntryComparer(AssetSortField field, bool descending) : IComparer<AssetIndexEntry>
    {
        public int Compare(AssetIndexEntry? left, AssetIndexEntry? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var value = field switch
            {
                AssetSortField.Name => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name),
                AssetSortField.Type => StringComparer.OrdinalIgnoreCase.Compare(left.TypeName, right.TypeName),
                AssetSortField.Size => left.ByteSize.CompareTo(right.ByteSize),
                AssetSortField.PathId => left.PathId.CompareTo(right.PathId),
                _ => left.Id.CompareTo(right.Id),
            };
            if (value == 0)
            {
                value = left.Id.CompareTo(right.Id);
            }
            return descending
                ? value > 0 ? -1 : value < 0 ? 1 : 0
                : value;
        }
    }

    private sealed class ReversedComparer<T>(IComparer<T> comparer) : IComparer<T>
    {
        public int Compare(T? left, T? right) => comparer.Compare(right!, left!);
    }
}
