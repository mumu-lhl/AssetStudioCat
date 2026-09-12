using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AssetStudio.AppCore.Indexing;

public sealed class DiskAssetIndex : IAssetIndex
{
    private const int SchemaVersion = 4;
    private const string MetadataFileName = "metadata.json";
    private const string CheckpointFileName = "checkpoint.json";
    private const string RowsFileName = "assets.jsonl";
    private const string OffsetsFileName = "assets.offsets";
    private readonly string _sourcePath;
    private readonly string _indexRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private volatile AssetIndexEntry[]? _cachedEntries;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private static readonly byte[] s_newline = "\n"u8.ToArray();

    public DiskAssetIndex(string indexesRoot, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        _sourcePath = Path.GetFullPath(sourcePath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_sourcePath)))[..24];
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

            await using (var rows = new FileStream(rowsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024))
            await using (var offsets = new FileStream(offsetsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                using var offsetsWriter = new BinaryWriter(offsets, Encoding.UTF8, leaveOpen: true);
                using var jsonWriter = new Utf8JsonWriter(rows);

                await foreach (var entry in entries.WithCancellation(cancellationToken))
                {
                    cached.Add(entry);
                    offsetsWriter.Write(rows.Position);
                    JsonSerializer.Serialize(jsonWriter, entry, _jsonOptions);
                    jsonWriter.Flush();
                    jsonWriter.Reset(rows);
                    rows.Write(s_newline);
                    count++;
                }

                offsetsWriter.Flush();
                await rows.FlushAsync(cancellationToken);
                await offsets.FlushAsync(cancellationToken);

                var checkpoint = new IndexCheckpoint(
                    SchemaVersion,
                    _sourcePath,
                    rows.Position,
                    offsets.Position,
                    count,
                    new Dictionary<string, IndexedFileInfo>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    DateTimeOffset.UtcNow);
                await using (var cpStream = File.Create(Path.Combine(staging, CheckpointFileName)))
                {
                    await JsonSerializer.SerializeAsync(cpStream, checkpoint, _jsonOptions, cancellationToken);
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

    public async Task<IndexResumeState> CheckResumeStateAsync(
        IReadOnlyList<string> sourceFiles,
        CancellationToken cancellationToken = default)
    {
        var checkpointPath = Path.Combine(_indexRoot, CheckpointFileName);
        var rowsPath = Path.Combine(_indexRoot, RowsFileName);
        var offsetsPath = Path.Combine(_indexRoot, OffsetsFileName);

        if (!File.Exists(checkpointPath) || !File.Exists(rowsPath) || !File.Exists(offsetsPath))
        {
            return new IndexResumeState(false, null, sourceFiles.ToList(), 0);
        }

        IndexCheckpoint? checkpoint;
        try
        {
            await using var stream = File.OpenRead(checkpointPath);
            checkpoint = await JsonSerializer.DeserializeAsync<IndexCheckpoint>(stream, _jsonOptions, cancellationToken);
        }
        catch
        {
            return new IndexResumeState(false, null, sourceFiles.ToList(), 0);
        }

        if (checkpoint is null || checkpoint.SchemaVersion != SchemaVersion)
        {
            return new IndexResumeState(false, null, sourceFiles.ToList(), 0);
        }

        var rowsInfo = new FileInfo(rowsPath);
        var offsetsInfo = new FileInfo(offsetsPath);
        if (rowsInfo.Length < checkpoint.RowsFileLength || offsetsInfo.Length < checkpoint.OffsetsFileLength)
        {
            return new IndexResumeState(false, null, sourceFiles.ToList(), 0);
        }

        // Verify that all previously indexed files still exist on disk with matching length and timestamp
        foreach (var (path, fileInfo) in checkpoint.IndexedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length != fileInfo.Length || fi.LastWriteTimeUtc.Ticks != fileInfo.LastWriteTimeUtcTicks)
            {
                return new IndexResumeState(false, null, sourceFiles.ToList(), 0);
            }
        }

        var remainingFiles = sourceFiles
            .Where(f => !checkpoint.IndexedFiles.ContainsKey(f))
            .ToList();

        return new IndexResumeState(true, checkpoint, remainingFiles, checkpoint.EntryCount);
    }

    public async Task<IndexAppender> CreateAppenderAsync(
        IndexResumeState resumeState,
        CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(_indexRoot)!;
        System.IO.Directory.CreateDirectory(parent);
        System.IO.Directory.CreateDirectory(_indexRoot);

        var rowsPath = Path.Combine(_indexRoot, RowsFileName);
        var offsetsPath = Path.Combine(_indexRoot, OffsetsFileName);

        if (resumeState.CanResume && resumeState.Checkpoint is { } cp)
        {
            var rowsStream = new FileStream(rowsPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024);
            var offsetsStream = new FileStream(offsetsPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 64 * 1024);

            // Truncate to the exact length of the last committed checkpoint to rollback any partial uncommitted batch
            rowsStream.SetLength(cp.RowsFileLength);
            rowsStream.Position = cp.RowsFileLength;

            offsetsStream.SetLength(cp.OffsetsFileLength);
            offsetsStream.Position = cp.OffsetsFileLength;

            return new IndexAppender(this, rowsStream, offsetsStream, cp, cp.EntryCount, _jsonOptions);
        }
        else
        {
            var rowsStream = new FileStream(rowsPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024);
            var offsetsStream = new FileStream(offsetsPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024);

            var freshCp = new IndexCheckpoint(
                SchemaVersion,
                _sourcePath,
                0,
                0,
                0,
                new Dictionary<string, IndexedFileInfo>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow);

            return new IndexAppender(this, rowsStream, offsetsStream, freshCp, 0, _jsonOptions);
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

    public sealed class IndexAppender : IAsyncDisposable
    {
        private readonly DiskAssetIndex _index;
        private readonly FileStream _rowsStream;
        private readonly FileStream _offsetsStream;
        private readonly BinaryWriter _offsetsWriter;
        private readonly Utf8JsonWriter _jsonWriter;
        private readonly JsonSerializerOptions _jsonOptions;
        private IndexCheckpoint _checkpoint;
        private long _entryCount;
        private bool _disposed;

        public IndexAppender(
            DiskAssetIndex index,
            FileStream rowsStream,
            FileStream offsetsStream,
            IndexCheckpoint checkpoint,
            long entryCount,
            JsonSerializerOptions jsonOptions)
        {
            _index = index;
            _rowsStream = rowsStream;
            _offsetsStream = offsetsStream;
            _offsetsWriter = new BinaryWriter(_offsetsStream, Encoding.UTF8, leaveOpen: true);
            _jsonWriter = new Utf8JsonWriter(_rowsStream);
            _checkpoint = checkpoint;
            _entryCount = entryCount;
            _jsonOptions = jsonOptions;
        }

        public long CurrentEntryCount => _entryCount;
        public Dictionary<string, string> Containers => _checkpoint.Containers;

        public void AppendEntry(AssetIndexEntry entry)
        {
            _offsetsWriter.Write(_rowsStream.Position);
            JsonSerializer.Serialize(_jsonWriter, entry, _jsonOptions);
            _jsonWriter.Flush();
            _jsonWriter.Reset(_rowsStream);
            _rowsStream.Write(s_newline);
            _entryCount++;
        }

        public async Task CommitBatchAsync(
            IReadOnlyList<string> batchFiles,
            long batchAssetCount,
            CancellationToken cancellationToken = default)
        {
            _offsetsWriter.Flush();
            await _rowsStream.FlushAsync(cancellationToken);
            await _offsetsStream.FlushAsync(cancellationToken);

            foreach (var file in batchFiles)
            {
                var fi = new FileInfo(file);
                if (fi.Exists)
                {
                    _checkpoint.IndexedFiles[file] = new IndexedFileInfo(fi.Length, fi.LastWriteTimeUtc.Ticks, batchAssetCount);
                }
            }

            _checkpoint = _checkpoint with
            {
                RowsFileLength = _rowsStream.Position,
                OffsetsFileLength = _offsetsStream.Position,
                EntryCount = _entryCount,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var checkpointPath = Path.Combine(_index.DirectoryPath, CheckpointFileName);
            var tmpPath = checkpointPath + ".tmp";
            await using (var cpStream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(cpStream, _checkpoint, _jsonOptions, cancellationToken);
            }
            File.Move(tmpPath, checkpointPath, overwrite: true);
        }

        public async Task FinalizeAsync(
            AssetSourceFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            _offsetsWriter.Flush();
            await _rowsStream.FlushAsync(cancellationToken);
            await _offsetsStream.FlushAsync(cancellationToken);

            _checkpoint = _checkpoint with
            {
                RowsFileLength = _rowsStream.Position,
                OffsetsFileLength = _offsetsStream.Position,
                EntryCount = _entryCount,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var checkpointPath = Path.Combine(_index.DirectoryPath, CheckpointFileName);
            var tmpCheckpointPath = checkpointPath + ".tmp";
            await using (var cpStream = File.Create(tmpCheckpointPath))
            {
                await JsonSerializer.SerializeAsync(cpStream, _checkpoint, _jsonOptions, cancellationToken);
            }
            File.Move(tmpCheckpointPath, checkpointPath, overwrite: true);

            var metadata = new IndexMetadata(SchemaVersion, fingerprint, _entryCount, DateTimeOffset.UtcNow);
            var metadataPath = Path.Combine(_index.DirectoryPath, MetadataFileName);
            var tmpMetadataPath = metadataPath + ".tmp";
            await using (var metadataStream = File.Create(tmpMetadataPath))
            {
                await JsonSerializer.SerializeAsync(metadataStream, metadata, _jsonOptions, cancellationToken);
            }
            File.Move(tmpMetadataPath, metadataPath, overwrite: true);

            _index._cachedEntries = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await _jsonWriter.DisposeAsync();
            _offsetsWriter.Dispose();
            await _rowsStream.DisposeAsync();
            await _offsetsStream.DisposeAsync();
        }
    }
}

public sealed record IndexResumeState(
    bool CanResume,
    IndexCheckpoint? Checkpoint,
    List<string> RemainingFiles,
    long ExistingEntryCount);

public sealed record IndexCheckpoint(
    int SchemaVersion,
    string SourceKey,
    long RowsFileLength,
    long OffsetsFileLength,
    long EntryCount,
    Dictionary<string, IndexedFileInfo> IndexedFiles,
    Dictionary<string, string> Containers,
    DateTimeOffset UpdatedAt);

public sealed record IndexedFileInfo(
    long Length,
    long LastWriteTimeUtcTicks,
    long AssetCount);
