namespace AssetStudio.AppCore.Indexing;

public sealed record SceneHierarchyNode(
    string Name,
    long GameObjectPathId,
    string SerializedFile,
    IReadOnlyList<SceneHierarchyNode> Children);
