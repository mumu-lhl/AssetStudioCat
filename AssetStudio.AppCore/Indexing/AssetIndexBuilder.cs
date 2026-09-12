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

        if (forceRebuild)
        {
            index.Rebuild();
        }

        var resolvedFiles = await Task.Run(() => AssetsManager.ResolveFilePaths(sourcePaths), cancellationToken);
        if (resolvedFiles.Count == 0)
        {
            await index.BuildAsync(
                fingerprint,
                EmptyEntries(),
                cancellationToken);
            return new AssetIndexBuildResult(
                index,
                fingerprint,
                false,
                0,
                new DecompressionModeSummary("Empty", "No source files found."));
        }

        var resumeState = forceRebuild
            ? new IndexResumeState(false, null, resolvedFiles, 0)
            : await index.CheckResumeStateAsync(resolvedFiles, cancellationToken);

        if (!resumeState.CanResume && !forceRebuild)
        {
            index.Rebuild();
        }

        var filesToProcess = resumeState.CanResume
            ? resumeState.RemainingFiles
            : resolvedFiles;

        await using var appender = await index.CreateAppenderAsync(resumeState, cancellationToken);

        if (filesToProcess.Count == 0)
        {
            await appender.FinalizeAsync(fingerprint, cancellationToken);
            return new AssetIndexBuildResult(
                index,
                fingerprint,
                false,
                appender.CurrentEntryCount,
                new DecompressionModeSummary("Incremental", "All files were already up to date in checkpoint."));
        }

        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (availableMemory <= 0) availableMemory = 4L * 1024 * 1024 * 1024;
        var fractionBudget = (long)(availableMemory * _settings.AutoMemoryFraction);
        var memoryBudget = Math.Min(_settings.AutoMemoryLimitBytes, fractionBudget);

        var batches = CreateFileBatches(filesToProcess, memoryBudget);

        var previousProgress = Progress.Default;
        var modeSummary = BundleDecompressionMode.Memory;
        var summaryReason = resumeState.CanResume && resumeState.ExistingEntryCount > 0
            ? $"Resumed/Incremental ({resolvedFiles.Count - filesToProcess.Count} files skipped, {filesToProcess.Count} processed)."
            : "In-memory streaming decompression.";

        try
        {
            if (progress is not null)
            {
                Progress.Default = progress;
            }

            var processedFileCount = resolvedFiles.Count - filesToProcess.Count;
            if (progress != null && resolvedFiles.Count > 0 && processedFileCount > 0)
            {
                progress.Report((int)(processedFileCount * 100.0 / resolvedFiles.Count));
            }

            for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = batches[batchIndex];

                long batchExpandedBytes = 0;
                foreach (var f in batch)
                {
                    try
                    {
                        batchExpandedBytes += SaturatingMultiply(new FileInfo(f).Length, 3);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                using var session = DecompressionSession.Create(_settings, _cacheLayout, batchExpandedBytes, availableMemory);
                if (session.Decision.EffectiveMode == BundleDecompressionMode.Disk)
                {
                    modeSummary = BundleDecompressionMode.Disk;
                    summaryReason = session.Decision.Reason;
                }

                using var managerScope = new AssetsManagerScope();
                managerScope.Manager.MetadataOnly = true;
                managerScope.Manager.Options.CustomUnityVersion = string.IsNullOrWhiteSpace(_settings.CustomUnityVersion)
                    ? null
                    : new UnityVersion(_settings.CustomUnityVersion);
                session.ApplyTo(managerScope.Manager.Options.BundleOptions);

                await Task.Run(() => managerScope.Manager.LoadFilesAndFolders(batch.ToArray()), cancellationToken);

                BuildContainerMap(managerScope.Manager, appender.Containers);

                long batchObjectCount = 0;
                foreach (var file in managerScope.Manager.AssetsFileList)
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

                        var containerKey = MakeContainerKey(file.fullName, location.m_PathID);
                        var entry = new AssetIndexEntry(
                            appender.CurrentEntryCount,
                            fingerprint.IndexKey,
                            file.originalPath ?? file.fullName,
                            file.fullName,
                            location.m_PathID,
                            location.classID,
                            classIdType.ToString(),
                            name,
                            appender.Containers.GetValueOrDefault(containerKey),
                            location.byteStart,
                            location.byteSize,
                            gameObjectPathId,
                            gameObjectSerializedFile,
                            parentTransformPathId,
                            parentTransformSerializedFile);

                        appender.AppendEntry(entry);
                        batchObjectCount++;

                        if ((appender.CurrentEntryCount & 511) == 0)
                        {
                            await Task.Yield();
                        }
                    }
                }

                await appender.CommitBatchAsync(batch, batchObjectCount, cancellationToken);
                processedFileCount += batch.Count;

                if (progress != null && resolvedFiles.Count > 0)
                {
                    var percent = (int)(processedFileCount * 100.0 / resolvedFiles.Count);
                    progress.Report(percent);
                }
            }

            await appender.FinalizeAsync(fingerprint, cancellationToken);
        }
        finally
        {
            Progress.Default = previousProgress;
        }

        return new AssetIndexBuildResult(
            index,
            fingerprint,
            false,
            appender.CurrentEntryCount,
            new DecompressionModeSummary(modeSummary.ToString(), summaryReason));
    }

    private static async IAsyncEnumerable<AssetIndexEntry> EmptyEntries()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static List<List<string>> CreateFileBatches(List<string> files, long memoryBudget)
    {
        var targetBatchBytes = Math.Clamp(memoryBudget / 3, 128L * 1024 * 1024, 512L * 1024 * 1024);
        var batches = new List<List<string>>();
        var currentBatch = new List<string>();
        long currentBatchEstimatedBytes = 0;

        foreach (var file in files)
        {
            long fileSize = 0;
            try
            {
                fileSize = new FileInfo(file).Length;
            }
            catch
            {
                // ignored
            }

            var expandedEstimate = SaturatingMultiply(fileSize, 3);
            if (currentBatch.Count > 0 && (currentBatchEstimatedBytes + expandedEstimate > targetBatchBytes || currentBatch.Count >= 50))
            {
                batches.Add(currentBatch);
                currentBatch = new List<string>();
                currentBatchEstimatedBytes = 0;
            }

            currentBatch.Add(file);
            currentBatchEstimatedBytes += expandedEstimate;
        }

        if (currentBatch.Count > 0)
        {
            batches.Add(currentBatch);
        }

        return batches;
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

    private static string MakeContainerKey(string serializedFile, long pathId) =>
        $"{Path.GetFileName(serializedFile).ToUpperInvariant()}:{pathId}";

    private static void BuildContainerMap(AssetsManager manager, Dictionary<string, string> containers)
    {
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
    }

    private static void AddContainer(
        Dictionary<string, string> containers,
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
            containers.TryAdd(MakeContainerKey(targetFile, pointer.m_PathID), container);
        }
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
