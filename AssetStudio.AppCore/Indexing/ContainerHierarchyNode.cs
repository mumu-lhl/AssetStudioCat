namespace AssetStudio.AppCore.Indexing;

public sealed record ContainerHierarchyNode(
    string Name,
    string FullPath,
    bool IsDirectory,
    int TotalAssetCount,
    AssetIndexEntry? AssetEntry,
    IReadOnlyList<ContainerHierarchyNode> Children);
