namespace AssetStudio.AppCore.Indexing;

public sealed class SceneHierarchyService
{
    public async Task<IReadOnlyList<SceneHierarchyNode>> BuildAsync(
        DiskAssetIndex index,
        CancellationToken cancellationToken = default)
    {
        var gameObjects = new Dictionary<ObjectKey, AssetIndexEntry>();
        var transforms = new List<AssetIndexEntry>();
        await foreach (var entry in index.EnumerateAsync(cancellationToken: cancellationToken))
        {
            if (entry.TypeName == "GameObject")
            {
                gameObjects[new ObjectKey(entry.SerializedFile, entry.PathId)] = entry;
            }
            else if (entry.TypeName is "Transform" or "RectTransform" && entry.GameObjectPathId is not null)
            {
                transforms.Add(entry);
            }
        }

        var nodes = new Dictionary<ObjectKey, MutableNode>();
        foreach (var transform in transforms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gameObjectKey = new ObjectKey(
                transform.GameObjectSerializedFile ?? transform.SerializedFile,
                transform.GameObjectPathId!.Value);
            var name = gameObjects.TryGetValue(gameObjectKey, out var gameObject)
                ? gameObject.Name
                : $"GameObject #{transform.GameObjectPathId}";
            nodes[new ObjectKey(transform.SerializedFile, transform.PathId)] = new MutableNode(
                name,
                transform.GameObjectPathId.Value,
                gameObject?.SerializedFile ?? transform.SerializedFile,
                transform);
        }

        var roots = new List<MutableNode>();
        foreach (var transform in transforms)
        {
            var key = new ObjectKey(transform.SerializedFile, transform.PathId);
            if (!nodes.TryGetValue(key, out var node))
            {
                continue;
            }
            if (transform.ParentTransformPathId is { } parentPathId
                && nodes.TryGetValue(
                    new ObjectKey(transform.ParentTransformSerializedFile ?? transform.SerializedFile, parentPathId),
                    out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }
        return roots.Select(ToImmutable).ToArray();
    }

    private static SceneHierarchyNode ToImmutable(MutableNode node) => new(
        node.Name,
        node.GameObjectPathId,
        node.SerializedFile,
        node.TransformEntry,
        node.Children.Select(ToImmutable).ToArray());

    private readonly record struct ObjectKey
    {
        public ObjectKey(string serializedFile, long pathId)
        {
            SerializedFileName = Path.GetFileName(serializedFile).ToUpperInvariant();
            PathId = pathId;
        }

        public string SerializedFileName { get; }

        public long PathId { get; }
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
