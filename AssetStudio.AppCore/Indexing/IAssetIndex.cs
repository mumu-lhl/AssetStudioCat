namespace AssetStudio.AppCore.Indexing;

public interface IAssetIndex
{
    string DirectoryPath { get; }

    Task BuildAsync(
        AssetSourceFingerprint fingerprint,
        IAsyncEnumerable<AssetIndexEntry> entries,
        CancellationToken cancellationToken = default);

    Task<bool> IsCurrentAsync(
        AssetSourceFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<AssetIndexPage> QueryAsync(
        AssetIndexQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, long>> GetTypeCountsAsync(
        CancellationToken cancellationToken = default);

    Task<AssetIndexEntry?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task WarmupAsync(
        CancellationToken cancellationToken = default);

    void Rebuild();
}
