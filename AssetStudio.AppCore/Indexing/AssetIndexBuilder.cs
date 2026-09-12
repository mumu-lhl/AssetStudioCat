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
        var containers = BuildContainerMap(manager);
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
                    containers.GetValueOrDefault(new ObjectReference(file.fullName, location.m_PathID)),
                    location.byteStart,
                    location.byteSize,
                    asset is Transform transform ? transform.m_GameObject.m_PathID : null,
                    asset is Transform gameObjectTransform
                        ? ResolveReferencedFile(file, gameObjectTransform.m_GameObject.m_FileID)
                        : null,
                    asset is Transform parentTransform && !parentTransform.m_Father.IsNull
                        ? parentTransform.m_Father.m_PathID
                        : null,
                    asset is Transform parentFileTransform && !parentFileTransform.m_Father.IsNull
                        ? ResolveReferencedFile(file, parentFileTransform.m_Father.m_FileID)
                        : null);

                if ((id & 255) == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    private static Dictionary<ObjectReference, string> BuildContainerMap(AssetsManager manager)
    {
        var containers = new Dictionary<ObjectReference, string>();
        foreach (var file in manager.AssetsFileList)
        {
            foreach (var location in file.m_Objects.Where(info =>
                         info.classID == (int)ClassIDType.AssetBundle
                         || info.classID == (int)ClassIDType.ResourceManager))
            {
                global::AssetStudio.Object? asset;
                try
                {
                    asset = manager.MaterializeObject(file, location);
                }
                catch
                {
                    continue;
                }

                if (asset is AssetBundle bundle)
                {
                    foreach (var pair in bundle.m_Container)
                    {
                        AddContainer(containers, file, pair.Value.asset, pair.Key);
                        var start = Math.Clamp(pair.Value.preloadIndex, 0, bundle.m_PreloadTable.Count);
                        var count = bundle.m_IsStreamedSceneAssetBundle
                            ? bundle.m_PreloadTable.Count - start
                            : Math.Clamp(pair.Value.preloadSize, 0, bundle.m_PreloadTable.Count - start);
                        for (var index = start; index < start + count; index++)
                        {
                            AddContainer(containers, file, bundle.m_PreloadTable[index], pair.Key);
                        }
                    }
                }
                else if (asset is ResourceManager resources)
                {
                    foreach (var pair in resources.m_Container)
                    {
                        AddContainer(containers, file, pair.Value, pair.Key);
                    }
                }
            }
        }
        return containers;
    }

    private static void AddContainer(
        IDictionary<ObjectReference, string> containers,
        SerializedFile sourceFile,
        PPtr<global::AssetStudio.Object> pointer,
        string container)
    {
        if (pointer.IsNull)
        {
            return;
        }
        var targetFile = ResolveReferencedFile(sourceFile, pointer.m_FileID);
        if (targetFile is not null)
        {
            containers.TryAdd(new ObjectReference(targetFile, pointer.m_PathID), container);
        }
    }

    private readonly record struct ObjectReference
    {
        public ObjectReference(string serializedFile, long pathId)
        {
            SerializedFileName = Path.GetFileName(serializedFile).ToUpperInvariant();
            PathId = pathId;
        }

        public string SerializedFileName { get; }
        public long PathId { get; }
    }

    private static string? ResolveReferencedFile(SerializedFile file, int fileId)
    {
        if (fileId == 0)
        {
            return file.fullName;
        }
        var index = fileId - 1;
        return index >= 0 && index < file.m_Externals.Count
            ? file.m_Externals[index].fileName
            : null;
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
