namespace AssetStudio.AppCore.Indexing;

public sealed record AssetIndexBuildResult(
    DiskAssetIndex Index,
    AssetSourceFingerprint Fingerprint,
    bool ReusedExistingIndex,
    long AssetCount,
    DecompressionModeSummary Decompression);

public sealed record DecompressionModeSummary(string Mode, string Reason);
