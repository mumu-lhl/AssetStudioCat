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
        var fingerprint = await Task.Run(
            () => AssetSourceFingerprint.Create(sourcePath, cancellationToken),
            cancellationToken);
        return await OpenCoreAsync([sourcePath], fingerprint, forceRebuild, progress, cancellationToken);
    }

    public async Task<AssetIndexBuildResult> OpenFilesAsync(
        IReadOnlyList<string> sourceFiles,
        bool forceRebuild = false,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = await Task.Run(
            () => AssetSourceFingerprint.CreateFiles(sourceFiles, cancellationToken),
            cancellationToken);
        return await OpenCoreAsync(sourceFiles, fingerprint, forceRebuild, progress, cancellationToken);
    }

    private async Task<AssetIndexBuildResult> OpenCoreAsync(
        IReadOnlyList<string> sourcePaths,
        AssetSourceFingerprint fingerprint,
        bool forceRebuild,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        _cacheLayout.EnsureCreated();
        var index = new DiskAssetIndex(_cacheLayout.Indexes, fingerprint.IndexKey);
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
        managerScope.Manager.Options.CustomUnityVersion = string.IsNullOrWhiteSpace(_settings.CustomUnityVersion)
            ? null
            : new UnityVersion(_settings.CustomUnityVersion);
        session.ApplyTo(managerScope.Manager.Options.BundleOptions);
        var previousProgress = Progress.Default;
        try
        {
            if (progress is not null)
            {
                Progress.Default = progress;
            }

            await Task.Run(
                () => managerScope.Manager.LoadFilesAndFolders(sourcePaths.ToArray()),
                cancellationToken);
            await index.BuildAsync(
                fingerprint,
                EnumerateEntries(managerScope.Manager, fingerprint.IndexKey, cancellationToken),
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
                var classIdType = (ClassIDType)location.classID;
                string? name = null;
                long? gameObjectPathId = null;
                string? gameObjectSerializedFile = null;
                long? parentTransformPathId = null;
                string? parentTransformSerializedFile = null;

                try
                {
                    if (classIdType is ClassIDType.Transform or ClassIDType.RectTransform)
                    {
                        var reader = new ObjectReader(file.reader, file, location);
                        var transform = new Transform(reader);
                        gameObjectPathId = transform.m_GameObject.m_PathID;
                        gameObjectSerializedFile = ResolveReferencedFile(file, transform.m_GameObject.m_FileID);
                        if (!transform.m_Father.IsNull)
                        {
                            parentTransformPathId = transform.m_Father.m_PathID;
                            parentTransformSerializedFile = ResolveReferencedFile(file, transform.m_Father.m_FileID);
                        }
                    }
                    else if (classIdType == ClassIDType.GameObject)
                    {
                        var reader = new ObjectReader(file.reader, file, location);
                        var go = new GameObject(reader);
                        name = go.m_Name;
                    }
                    else if (classIdType == ClassIDType.MonoBehaviour)
                    {
                        var reader = new ObjectReader(file.reader, file, location);
                        var mb = new MonoBehaviour(reader);
                        name = mb.m_Name;
                    }
                    else if (classIdType == ClassIDType.AssetBundle)
                    {
                        var reader = new ObjectReader(file.reader, file, location);
                        var ab = new AssetBundle(reader);
                        name = string.IsNullOrEmpty(ab.m_AssetBundleName) ? ab.m_Name : ab.m_AssetBundleName;
                    }
                    else if (IsNamedObjectClass(classIdType))
                    {
                        var reader = new ObjectReader(file.reader, file, location);
                        var named = new NamedObject(reader);
                        name = named.m_Name;
                    }
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Unable to index {file.fileName} PathID {location.m_PathID}: {exception.Message}");
                }

                if (string.IsNullOrEmpty(name))
                {
                    name = $"{classIdType} #{location.m_PathID}";
                }

                yield return new AssetIndexEntry(
                    id++,
                    sourceRoot,
                    file.originalPath ?? file.fullName,
                    file.fullName,
                    location.m_PathID,
                    location.classID,
                    classIdType.ToString(),
                    name,
                    containers.GetValueOrDefault(new ObjectReference(file.fullName, location.m_PathID)),
                    location.byteStart,
                    location.byteSize,
                    gameObjectPathId,
                    gameObjectSerializedFile,
                    parentTransformPathId,
                    parentTransformSerializedFile);

                if ((id & 511) == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    private static bool IsNamedObjectClass(ClassIDType type) => type switch
    {
        ClassIDType.Texture2D or
        ClassIDType.Texture2DArray or
        ClassIDType.MovieTexture or
        ClassIDType.Mesh or
        ClassIDType.Shader or
        ClassIDType.Material or
        ClassIDType.AnimationClip or
        ClassIDType.AudioClip or
        ClassIDType.VideoClip or
        ClassIDType.TextAsset or
        ClassIDType.Sprite or
        ClassIDType.SpriteAtlas or
        ClassIDType.Avatar or
        ClassIDType.Font or
        ClassIDType.MonoScript or
        ClassIDType.PreloadData or
        ClassIDType.AnimatorController or
        ClassIDType.AnimatorOverrideController or
        ClassIDType.ComputeShader or
        ClassIDType.ShaderVariantCollection => true,
        _ => false
    };

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


    private static long SaturatingMultiply(long value, int multiplier) =>
        value > long.MaxValue / multiplier ? long.MaxValue : value * multiplier;

    private sealed class AssetsManagerScope : IDisposable
    {
        public AssetsManager Manager { get; } = new();

        public void Dispose() => Manager.Clear();
    }
}
