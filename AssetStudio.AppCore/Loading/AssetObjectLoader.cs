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
        CancellationToken cancellationToken = default) => Task.Run(() => Open(entry, false, cancellationToken), cancellationToken);

    public Task<AssetObjectSession> OpenDependencyGraphAsync(
        AssetIndexEntry entry,
        CancellationToken cancellationToken = default) => Task.Run(() => Open(entry, true, cancellationToken), cancellationToken);

    private AssetObjectSession Open(AssetIndexEntry entry, bool materializeAnimatorGraph, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceInfo = new FileInfo(entry.ObjectSourcePath);
        var estimatedExpandedBytes = sourceInfo.Exists && sourceInfo.Length <= long.MaxValue / 3
            ? sourceInfo.Length * 3
            : long.MaxValue;
        var decompression = DecompressionSession.Create(_settings, _cacheLayout, estimatedExpandedBytes);
        var manager = new AssetsManager { MetadataOnly = true };
        if (materializeAnimatorGraph)
        {
            var rootType = Enum.IsDefined(typeof(ClassIDType), entry.ClassId)
                ? (ClassIDType)entry.ClassId
                : ClassIDType.Object;
            manager.SetAssetFilter(rootType);
            if (rootType == ClassIDType.Animator)
            {
                manager.SetAssetFilter(ClassIDType.Mesh, ClassIDType.Texture2D, ClassIDType.Shader);
            }
        }
        decompression.ApplyTo(manager.Options.BundleOptions);

        try
        {
            manager.LoadFilesAndFolders(entry.ObjectSourcePath);
            if (materializeAnimatorGraph)
            {
                ResolveAndLoadDependencies(manager, entry, cancellationToken);
                manager.MaterializeLoadedAssets();
            }
            cancellationToken.ThrowIfCancellationRequested();
            var serializedFile = manager.AssetsFileList.FirstOrDefault(file =>
                PathsEqual(file.fullName, entry.SerializedFile));
            if (serializedFile is null)
            {
                throw new InvalidDataException($"Serialized file is no longer present: {entry.SerializedFile}");
            }

            global::AssetStudio.Object? asset;
            if (materializeAnimatorGraph)
            {
                serializedFile.ObjectsDic.TryGetValue(entry.PathId, out asset);
            }
            else
            {
                var objectInfo = serializedFile.m_Objects.FirstOrDefault(info => info.m_PathID == entry.PathId)
                    ?? throw new InvalidDataException($"PathID {entry.PathId} is no longer present.");
                asset = manager.MaterializeObject(serializedFile, objectInfo);
            }
            if (asset is null)
            {
                throw new InvalidDataException($"Object {entry.PathId} cannot be materialized.");
            }
            return new AssetObjectSession(manager, decompression, asset);
        }
        catch
        {
            manager.Clear();
            decompression.Dispose();
            throw;
        }
    }

    private void ResolveAndLoadDependencies(
        AssetsManager manager,
        AssetIndexEntry entry,
        CancellationToken cancellationToken)
    {
        var index = new DiskAssetIndex(_cacheLayout.Indexes, entry.SourcePath);
        var loadedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(entry.ObjectSourcePath),
        };
        var inspectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var externalNames = manager.AssetsFileList
                .Where(file => inspectedFiles.Add(file.fullName))
                .SelectMany(file => file.m_Externals)
                .Select(external => external.fileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (externalNames.Length == 0)
            {
                return;
            }

            var resolvedSources = index.ResolveObjectSourcesAsync(externalNames, cancellationToken)
                .GetAwaiter()
                .GetResult();
            var newSources = resolvedSources
                .Select(Path.GetFullPath)
                .Where(loadedSources.Add)
                .ToArray();
            if (newSources.Length == 0)
            {
                return;
            }
            manager.LoadFilesAndFolders(newSources);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
