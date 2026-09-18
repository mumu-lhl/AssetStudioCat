using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;

namespace AssetStudio.AppCore.Exporting;

public sealed class GameObjectExportService
{
    private static readonly object FbxExportLock = new();
    private readonly AssetObjectLoader _objectLoader;
    private readonly AppSettings _settings;

    public GameObjectExportService(AssetObjectLoader objectLoader, AppSettings settings)
    {
        _objectLoader = objectLoader;
        _settings = settings;
    }

    public Task<AssetExportResult> ExportAsync(
        AssetIndexEntry transformEntry,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(transformEntry, outputDirectory, cancellationToken), cancellationToken);

    public Task<AssetExportResult> ExportGltfAsync(
        AssetIndexEntry transformEntry,
        string outputDirectory,
        Gltf.Format? format = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExportGltf(transformEntry, outputDirectory, format, cancellationToken), cancellationToken);

    private AssetExportResult ExportGltf(
        AssetIndexEntry transformEntry,
        string outputDirectory,
        Gltf.Format? format,
        CancellationToken cancellationToken)
    {
        if (transformEntry.TypeName is not "Transform" and not "RectTransform")
        {
            throw new NotSupportedException("Scene glTF export requires a Transform or RectTransform index entry.");
        }

        using var session = _objectLoader.OpenDependencyGraphAsync(transformEntry, cancellationToken).GetAwaiter().GetResult();
        if (session.Asset is not Transform transform || !transform.m_GameObject.TryGet(out var gameObject))
        {
            throw new InvalidDataException("The selected scene node's GameObject could not be resolved.");
        }

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var safeName = MakeSafeFileName(gameObject.m_Name);
        var finalDirectory = GetAvailableDirectory(root, safeName, transformEntry.PathId);
        var temporaryDirectory = Path.Combine(root, $".{safeName}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var exportFormat = format ?? _settings.GltfFormat;
            var ext = Gltf.Settings.GetFileExtension(exportFormat);
            var gltfPath = Path.Combine(temporaryDirectory, safeName + ext);
            var converter = new ModelConverter(gameObject, ToImageFormat(_settings.ConvertedImageFormat));
            var gltfSettings = new Gltf.Settings
            {
                ExportFormat = exportFormat,
                ExportAnimations = _settings.GltfExportAnimations,
                ExportSkins = _settings.GltfExportSkins,
                ExportBlendShapes = _settings.GltfExportBlendShapes,
                ScaleFactor = (float)_settings.GltfScaleFactor,
            };
            ModelExporter.ExportGltf(gltfPath, converter, gltfSettings);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporaryDirectory, finalDirectory);
            return new AssetExportResult(Directory.GetFiles(finalDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    private AssetExportResult Export(
        AssetIndexEntry transformEntry,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (transformEntry.TypeName is not "Transform" and not "RectTransform")
        {
            throw new NotSupportedException("Scene FBX export requires a Transform or RectTransform index entry.");
        }

        using var session = _objectLoader.OpenDependencyGraphAsync(transformEntry, cancellationToken).GetAwaiter().GetResult();
        if (session.Asset is not Transform transform || !transform.m_GameObject.TryGet(out var gameObject))
        {
            throw new InvalidDataException("The selected scene node's GameObject could not be resolved.");
        }

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var safeName = MakeSafeFileName(gameObject.m_Name);
        var finalDirectory = GetAvailableDirectory(root, safeName, transformEntry.PathId);
        var temporaryDirectory = Path.Combine(root, $".{safeName}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var fbxPath = Path.Combine(temporaryDirectory, safeName + ".fbx");
            var converter = new ModelConverter(gameObject, ToImageFormat(_settings.ConvertedImageFormat));
            lock (FbxExportLock)
            {
                ModelExporter.ExportFbx(fbxPath, converter, CreateFbxSettings());
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporaryDirectory, finalDirectory);
            return new AssetExportResult(Directory.GetFiles(finalDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    private Fbx.Settings CreateFbxSettings() => new()
    {
        ExportAnimations = _settings.FbxExportAnimations,
        ExportSkins = _settings.FbxExportSkins,
        ExportBlendShape = _settings.FbxExportBlendShapes,
        ExportAllNodes = _settings.FbxExportAllNodes,
        EulerFilter = _settings.FbxEulerFilter,
        ScaleFactor = (float)_settings.FbxScaleFactor,
        FbxFormat = _settings.FbxAscii ? 1 : 0,
    };

    private static string GetAvailableDirectory(string root, string name, long pathId)
    {
        var candidate = Path.Combine(root, name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        candidate = Path.Combine(root, $"{name}_{pathId}");
        return Directory.Exists(candidate) || File.Exists(candidate)
            ? Path.Combine(root, $"{name}_{pathId}_{Guid.NewGuid():N}")
            : candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        const string invalid = "<>:\"/\\|?*";
        var safe = new string(value.Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "GameObject" : safe;
    }

    private static ImageFormat ToImageFormat(ConvertedImageFormat format) => format switch
    {
        ConvertedImageFormat.Jpeg => ImageFormat.Jpeg,
        ConvertedImageFormat.Webp => ImageFormat.Webp,
        ConvertedImageFormat.Bmp => ImageFormat.Bmp,
        ConvertedImageFormat.Tga => ImageFormat.Tga,
        _ => ImageFormat.Png,
    };
}
