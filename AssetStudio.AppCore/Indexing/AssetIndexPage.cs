namespace AssetStudio.AppCore.Indexing;

public sealed record AssetIndexPage(
    IReadOnlyList<AssetIndexEntry> Items,
    int Offset,
    int? NextOffset,
    long? TotalCount);
