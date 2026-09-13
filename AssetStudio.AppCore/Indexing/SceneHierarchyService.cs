using System.IO;
using System.Text;

namespace AssetStudio.AppCore.Indexing;

public sealed class SceneHierarchyService
{
    private const uint CacheMagic = 0x4353434E; // "CSCN"
    private const uint CacheVersion = 1;
    private const string CacheFileName = "scenes.bin";

    public async Task<IReadOnlyList<SceneHierarchyNode>> BuildAsync(
        DiskAssetIndex index,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(index.DirectoryPath, CacheFileName);
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = await TryLoadFromCacheAsync(cachePath, cancellationToken);
                if (cached is not null)
                {
                    return cached;
                }
            }
            catch
            {
                // Fallback to building from index entries
            }
        }

        var result = await BuildFromIndexAsync(index, cancellationToken);

        try
        {
            await SaveToCacheAsync(cachePath, result, cancellationToken);
        }
        catch
        {
            // Non-fatal if cache save fails
        }

        return result;
    }

    public async Task<IReadOnlyList<SceneHierarchyNode>> BuildFromIndexAsync(
        DiskAssetIndex index,
        CancellationToken cancellationToken = default)
    {
        var pool = new FileNamePool();
        var gameObjects = new Dictionary<ObjectKey, AssetIndexEntry>();
        var transforms = new List<AssetIndexEntry>();

        await foreach (var entry in index.EnumerateAsync(cancellationToken: cancellationToken))
        {
            if (entry.TypeName == "GameObject")
            {
                var norm = pool.GetNormalized(entry.SerializedFile);
                gameObjects[new ObjectKey(norm, entry.PathId)] = entry;
            }
            else if (entry.TypeName is "Transform" or "RectTransform" && entry.GameObjectPathId is not null)
            {
                transforms.Add(entry);
            }
        }

        var nodes = new Dictionary<ObjectKey, MutableNode>(transforms.Count);
        foreach (var transform in transforms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var goFile = transform.GameObjectSerializedFile ?? transform.SerializedFile;
            var gameObjectKey = new ObjectKey(
                pool.GetNormalized(goFile),
                transform.GameObjectPathId!.Value);

            var name = gameObjects.TryGetValue(gameObjectKey, out var gameObject)
                ? gameObject.Name
                : $"GameObject #{transform.GameObjectPathId}";

            var transformKey = new ObjectKey(
                pool.GetNormalized(transform.SerializedFile),
                transform.PathId);

            nodes[transformKey] = new MutableNode(
                name,
                transform.GameObjectPathId.Value,
                gameObject?.SerializedFile ?? transform.SerializedFile,
                transform);
        }

        var roots = new List<MutableNode>();
        foreach (var transform in transforms)
        {
            var key = new ObjectKey(pool.GetNormalized(transform.SerializedFile), transform.PathId);
            if (!nodes.TryGetValue(key, out var node))
            {
                continue;
            }

            if (transform.ParentTransformPathId is { } parentPathId)
            {
                var parentFile = transform.ParentTransformSerializedFile ?? transform.SerializedFile;
                var parentKey = new ObjectKey(pool.GetNormalized(parentFile), parentPathId);
                if (nodes.TryGetValue(parentKey, out var parent))
                {
                    parent.Children.Add(node);
                    continue;
                }
            }

            roots.Add(node);
        }

        roots.Sort(NodeComparer.Instance);
        var result = new SceneHierarchyNode[roots.Count];
        for (int i = 0; i < roots.Count; i++)
        {
            result[i] = ToImmutable(roots[i]);
        }
        return result;
    }

    private static SceneHierarchyNode ToImmutable(MutableNode node)
    {
        SceneHierarchyNode[] children;
        if (node.Children.Count == 0)
        {
            children = [];
        }
        else
        {
            if (node.Children.Count > 1)
            {
                node.Children.Sort(NodeComparer.Instance);
            }

            children = new SceneHierarchyNode[node.Children.Count];
            for (int i = 0; i < node.Children.Count; i++)
            {
                children[i] = ToImmutable(node.Children[i]);
            }
        }

        return new SceneHierarchyNode(
            node.Name,
            node.GameObjectPathId,
            node.SerializedFile,
            node.TransformEntry,
            children);
    }

    private static async Task SaveToCacheAsync(
        string cachePath,
        IReadOnlyList<SceneHierarchyNode> roots,
        CancellationToken cancellationToken)
    {
        var tmpPath = cachePath + ".tmp";
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stringMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var stringList = new List<string>();

            int GetStringIndex(string? s)
            {
                if (s is null) return -1;
                if (stringMap.TryGetValue(s, out var idx)) return idx;
                idx = stringList.Count;
                stringList.Add(s);
                stringMap[s] = idx;
                return idx;
            }

            void CollectStrings(SceneHierarchyNode node)
            {
                GetStringIndex(node.Name);
                GetStringIndex(node.SerializedFile);
                var e = node.TransformEntry;
                GetStringIndex(e.SourcePath);
                GetStringIndex(e.ObjectSourcePath);
                GetStringIndex(e.SerializedFile);
                GetStringIndex(e.TypeName);
                GetStringIndex(e.Name);
                GetStringIndex(e.Container);
                GetStringIndex(e.GameObjectSerializedFile);
                GetStringIndex(e.ParentTransformSerializedFile);

                for (int i = 0; i < node.Children.Count; i++)
                {
                    CollectStrings(node.Children[i]);
                }
            }

            for (int i = 0; i < roots.Count; i++)
            {
                CollectStrings(roots[i]);
            }

            using var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 128 * 1024);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

            writer.Write(CacheMagic);
            writer.Write(CacheVersion);
            writer.Write(roots.Count);
            writer.Write(stringList.Count);

            for (int i = 0; i < stringList.Count; i++)
            {
                writer.Write(stringList[i]);
            }

            void WriteNode(SceneHierarchyNode node)
            {
                writer.Write(GetStringIndex(node.Name));
                writer.Write(node.GameObjectPathId);
                writer.Write(GetStringIndex(node.SerializedFile));

                var e = node.TransformEntry;
                writer.Write(e.Id);
                writer.Write(GetStringIndex(e.SourcePath));
                writer.Write(GetStringIndex(e.ObjectSourcePath));
                writer.Write(GetStringIndex(e.SerializedFile));
                writer.Write(e.PathId);
                writer.Write(e.ClassId);
                writer.Write(GetStringIndex(e.TypeName));
                writer.Write(GetStringIndex(e.Name));
                writer.Write(GetStringIndex(e.Container));
                writer.Write(e.ByteStart);
                writer.Write(e.ByteSize);
                writer.Write(e.GameObjectPathId.HasValue);
                if (e.GameObjectPathId.HasValue) writer.Write(e.GameObjectPathId.Value);
                writer.Write(GetStringIndex(e.GameObjectSerializedFile));
                writer.Write(e.ParentTransformPathId.HasValue);
                if (e.ParentTransformPathId.HasValue) writer.Write(e.ParentTransformPathId.Value);
                writer.Write(GetStringIndex(e.ParentTransformSerializedFile));

                writer.Write(node.Children.Count);
                for (int i = 0; i < node.Children.Count; i++)
                {
                    WriteNode(node.Children[i]);
                }
            }

            for (int i = 0; i < roots.Count; i++)
            {
                WriteNode(roots[i]);
            }
        }, cancellationToken);

        File.Move(tmpPath, cachePath, overwrite: true);
    }

    private static async Task<IReadOnlyList<SceneHierarchyNode>?> TryLoadFromCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        return await Task.Run<IReadOnlyList<SceneHierarchyNode>?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            if (reader.ReadUInt32() != CacheMagic) return null;
            if (reader.ReadUInt32() != CacheVersion) return null;

            int rootCount = reader.ReadInt32();
            if (rootCount < 0 || rootCount > 10_000_000) return null;

            int stringCount = reader.ReadInt32();
            if (stringCount < 0 || stringCount > 10_000_000) return null;

            var stringTable = new string[stringCount];
            for (int i = 0; i < stringCount; i++)
            {
                stringTable[i] = reader.ReadString();
            }

            string? GetStr(int idx) => idx >= 0 && idx < stringTable.Length ? stringTable[idx] : null;
            string GetNonNullStr(int idx) => idx >= 0 && idx < stringTable.Length ? stringTable[idx] : string.Empty;

            SceneHierarchyNode ReadNode()
            {
                var name = GetNonNullStr(reader.ReadInt32());
                var goPathId = reader.ReadInt64();
                var serFile = GetNonNullStr(reader.ReadInt32());

                var id = reader.ReadInt64();
                var sourcePath = GetNonNullStr(reader.ReadInt32());
                var objectSourcePath = GetNonNullStr(reader.ReadInt32());
                var tfSerFile = GetNonNullStr(reader.ReadInt32());
                var pathId = reader.ReadInt64();
                var classId = reader.ReadInt32();
                var typeName = GetNonNullStr(reader.ReadInt32());
                var tfName = GetNonNullStr(reader.ReadInt32());
                var container = GetStr(reader.ReadInt32());
                var byteStart = reader.ReadInt64();
                var byteSize = reader.ReadUInt32();

                long? goPid = reader.ReadBoolean() ? reader.ReadInt64() : null;
                var goSerFile = GetStr(reader.ReadInt32());

                long? parentPid = reader.ReadBoolean() ? reader.ReadInt64() : null;
                var parentSerFile = GetStr(reader.ReadInt32());

                var entry = new AssetIndexEntry(
                    id, sourcePath, objectSourcePath, tfSerFile, pathId, classId, typeName, tfName,
                    container, byteStart, byteSize, goPid, goSerFile, parentPid, parentSerFile);

                int childCount = reader.ReadInt32();
                SceneHierarchyNode[] children;
                if (childCount <= 0)
                {
                    children = [];
                }
                else
                {
                    children = new SceneHierarchyNode[childCount];
                    for (int i = 0; i < childCount; i++)
                    {
                        children[i] = ReadNode();
                    }
                }

                return new SceneHierarchyNode(name, goPathId, serFile, entry, children);
            }

            var roots = new SceneHierarchyNode[rootCount];
            for (int i = 0; i < rootCount; i++)
            {
                roots[i] = ReadNode();
            }
            return roots;
        }, cancellationToken);
    }

    private sealed class NodeComparer : IComparer<MutableNode>
    {
        public static readonly NodeComparer Instance = new();

        public int Compare(MutableNode? x, MutableNode? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int xHasChildren = x.Children.Count > 0 ? 1 : 0;
            int yHasChildren = y.Children.Count > 0 ? 1 : 0;
            int cmp = yHasChildren.CompareTo(xHasChildren);
            if (cmp != 0) return cmp;

            return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class FileNamePool
    {
        private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

        public string GetNormalized(string path)
        {
            if (_cache.TryGetValue(path, out var norm)) return norm;
            norm = Path.GetFileName(path).ToUpperInvariant();
            _cache[path] = norm;
            return norm;
        }
    }

    private readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        public ObjectKey(string serializedFileName, long pathId)
        {
            SerializedFileName = serializedFileName;
            PathId = pathId;
        }

        public string SerializedFileName { get; }
        public long PathId { get; }

        public bool Equals(ObjectKey other) =>
            PathId == other.PathId && string.Equals(SerializedFileName, other.SerializedFileName, StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is ObjectKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(SerializedFileName, PathId);
    }

    private sealed class MutableNode(
        string name,
        long gameObjectPathId,
        string serializedFile,
        AssetIndexEntry transformEntry)
    {
        public string Name { get; } = name;
        public long GameObjectPathId { get; } = gameObjectPathId;
        public string SerializedFile { get; } = serializedFile;
        public AssetIndexEntry TransformEntry { get; } = transformEntry;
        public List<MutableNode> Children { get; } = [];
    }
}
