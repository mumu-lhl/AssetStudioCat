using System.Runtime.CompilerServices;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;

namespace AssetStudio.AppCore.Indexing;

public sealed class AssetIndexBuilder
{
    private readonly AppSettings _settings;
    private readonly CacheLayout _cacheLayout;

    public AssetIndexBuilder(AppSettings settings, CacheLayout cacheLayout)
    {
        _settings = settings;
        _cacheLayout = cacheLayout;
    }

    public async Task<AssetIndexBuildResult> OpenAsync(
        string sourcePath,
        bool forceRebuild = false,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _cacheLayout.EnsureCreated();
        var fingerprint = await Task.Run(
            () => AssetSourceFingerprint.Create(sourcePath, cancellationToken),
            cancellationToken);
        var index = new DiskAssetIndex(_cacheLayout.Indexes, fingerprint.RootPath);
        if (!forceRebuild && await index.IsCurrentAsync(fingerprint, cancellationToken))
        {
            var page = await index.QueryAsync(new AssetIndexQuery(Limit: 1), cancellationToken);
            return new AssetIndexBuildResult(
                index,
                fingerprint,
                true,
                page.TotalCount ?? 0,
                new DecompressionModeSummary("Cached", "No bundles were decompressed."));
        }

        index.Rebuild();
        var estimatedExpandedBytes = SaturatingMultiply(fingerprint.TotalBytes, 3);
        using var session = DecompressionSession.Create(_settings, _cacheLayout, estimatedExpandedBytes);
        using var managerScope = new AssetsManagerScope();
        managerScope.Manager.MetadataOnly = true;
        session.ApplyTo(managerScope.Manager.Options.BundleOptions);
        var previousProgress = Progress.Default;
        try
        {
            if (progress is not null)
            {
                Progress.Default = progress;
            }

            await Task.Run(
                () => managerScope.Manager.LoadFilesAndFolders(fingerprint.RootPath),
                cancellationToken);
            await index.BuildAsync(
                fingerprint,
                EnumerateEntries(managerScope.Manager, fingerprint.RootPath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            Progress.Default = previousProgress;
        }

        var count = managerScope.Manager.AssetsFileList.Sum(file => (long)file.m_Objects.Count);
        return new AssetIndexBuildResult(
            index,
            fingerprint,
            false,
            count,
            new DecompressionModeSummary(session.Decision.EffectiveMode.ToString(), session.Decision.Reason));
    }

    private static async IAsyncEnumerable<AssetIndexEntry> EnumerateEntries(
        AssetsManager manager,
        string sourceRoot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long id = 0;
        foreach (var file in manager.AssetsFileList)
        {
            foreach (var location in file.m_Objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                global::AssetStudio.Object? asset = null;
                try
                {
                    asset = manager.MaterializeObject(file, location);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Unable to index {file.fileName} PathID {location.m_PathID}: {exception.Message}");
                }

                yield return new AssetIndexEntry(
                    id++,
                    sourceRoot,
                    file.originalPath ?? file.fullName,
                    file.fullName,
                    location.m_PathID,
                    location.classID,
                    ((ClassIDType)location.classID).ToString(),
                    asset is null ? $"{(ClassIDType)location.classID} #{location.m_PathID}" : ResolveName(asset),
                    null,
                    location.byteStart,
                    location.byteSize);

                if ((id & 255) == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    private static string ResolveName(global::AssetStudio.Object asset)
    {
        var name = asset switch
        {
            GameObject value => value.m_Name,
            MonoBehaviour value => value.m_Name,
            Shader value => value.m_ParsedForm?.m_Name ?? value.m_Name,
            AssetBundle value => string.IsNullOrEmpty(value.m_AssetBundleName) ? value.m_Name : value.m_AssetBundleName,
            NamedObject value => value.m_Name,
            _ => asset.Name,
        };
        return string.IsNullOrEmpty(name) ? $"{asset.type} #{asset.m_PathID}" : name;
    }

    private static long SaturatingMultiply(long value, int multiplier) =>
        value > long.MaxValue / multiplier ? long.MaxValue : value * multiplier;

    private sealed class AssetsManagerScope : IDisposable
    {
        public AssetsManager Manager { get; } = new();

        public void Dispose() => Manager.Clear();
    }
}
