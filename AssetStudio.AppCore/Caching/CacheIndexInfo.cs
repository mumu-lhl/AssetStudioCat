namespace AssetStudio.AppCore.Caching;

public sealed record CacheIndexInfo(
    string DirectoryPath,
    string SourcePath,
    int SourceFileCount,
    long SourceBytes,
    long AssetCount,
    long CacheBytes,
    DateTimeOffset CreatedAt);
