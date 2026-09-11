using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using global::AssetStudio;

namespace AssetStudio.AppCore.Loading;

public sealed class AssetObjectLoader
{
    private readonly AppSettings _settings;
    private readonly CacheLayout _cacheLayout;

    public AssetObjectLoader(AppSettings settings, CacheLayout cacheLayout)
    {
        _settings = settings;
        _cacheLayout = cacheLayout;
    }

    public Task<AssetObjectSession> OpenAsync(
        AssetIndexEntry entry,
        CancellationToken cancellationToken = default) => Task.Run(() => Open(entry, cancellationToken), cancellationToken);

    private AssetObjectSession Open(AssetIndexEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceInfo = new FileInfo(entry.ObjectSourcePath);
        var estimatedExpandedBytes = sourceInfo.Exists && sourceInfo.Length <= long.MaxValue / 3
            ? sourceInfo.Length * 3
            : long.MaxValue;
        var decompression = DecompressionSession.Create(_settings, _cacheLayout, estimatedExpandedBytes);
        var manager = new AssetsManager { MetadataOnly = true };
        decompression.ApplyTo(manager.Options.BundleOptions);

        try
        {
            manager.LoadFilesAndFolders(entry.ObjectSourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var serializedFile = manager.AssetsFileList.FirstOrDefault(file =>
                PathsEqual(file.fullName, entry.SerializedFile));
            if (serializedFile is null)
            {
                throw new InvalidDataException($"Serialized file is no longer present: {entry.SerializedFile}");
            }

            var objectInfo = serializedFile.m_Objects.FirstOrDefault(info => info.m_PathID == entry.PathId)
                ?? throw new InvalidDataException($"PathID {entry.PathId} is no longer present.");
            var asset = manager.MaterializeObject(serializedFile, objectInfo)
                ?? throw new InvalidDataException($"Object {entry.PathId} cannot be materialized.");
            return new AssetObjectSession(manager, decompression, asset);
        }
        catch
        {
            manager.Clear();
            decompression.Dispose();
            throw;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
