using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using AssetStudio.AppCore.Configuration;
using global::AssetStudio;

namespace AssetStudio.AppCore.Exporting;

public sealed class AnimatorExportService
{
    private static readonly object FbxExportLock = new();
    private readonly AssetObjectLoader _objectLoader;
    private readonly AppSettings _appSettings;

    public AnimatorExportService(AssetObjectLoader objectLoader, AppSettings appSettings)
    {
        _objectLoader = objectLoader;
        _appSettings = appSettings;
    }

    public Task<AssetExportResult> ExportAsync(
        AssetIndexEntry entry,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(entry, outputDirectory, cancellationToken), cancellationToken);

    private AssetExportResult Export(
        AssetIndexEntry entry,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!entry.TypeName.Equals(nameof(ClassIDType.Animator), StringComparison.Ordinal))
        {
            throw new NotSupportedException("FBX export requires an Animator asset.");
        }

        using var session = _objectLoader.OpenDependencyGraphAsync(entry, cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        if (session.Asset is not Animator animator)
        {
            throw new InvalidDataException($"PathID {entry.PathId} is not an Animator.");
        }

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var safeName = MakeSafeFileName(entry.Name);
        var finalDirectory = GetAvailableDirectory(root, safeName, entry.PathId);
        var temporaryDirectory = Path.Combine(root, $".{safeName}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var temporaryFbx = Path.Combine(temporaryDirectory, safeName + ".fbx");
            var converter = new ModelConverter(animator, ToImageFormat(_appSettings.ConvertedImageFormat));
            var settings = new Fbx.Settings
            {
                ExportAnimations = _appSettings.FbxExportAnimations,
                ExportSkins = _appSettings.FbxExportSkins,
                ExportBlendShape = _appSettings.FbxExportBlendShapes,
                ExportAllNodes = _appSettings.FbxExportAllNodes,
                EulerFilter = _appSettings.FbxEulerFilter,
                ScaleFactor = (float)_appSettings.FbxScaleFactor,
                FbxFormat = _appSettings.FbxAscii ? 1 : 0,
            };
            lock (FbxExportLock)
            {
                var previousDirectory = Directory.GetCurrentDirectory();
                try
                {
                    ModelExporter.ExportFbx(temporaryFbx, converter, settings);
                }
                finally
                {
                    Directory.SetCurrentDirectory(previousDirectory);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporaryDirectory, finalDirectory);
            return new AssetExportResult(Directory.GetFiles(finalDirectory, "*", SearchOption.AllDirectories));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Animator export failed. The Animator may reference objects in another AssetBundle, or the FBX native library may be unavailable.",
                exception);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }
    }

    private static string GetAvailableDirectory(string root, string name, long pathId)
    {
        var candidate = Path.Combine(root, name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.Combine(root, $"{name}_{pathId}");
        return Directory.Exists(candidate) || File.Exists(candidate)
            ? Path.Combine(root, $"{name}_{pathId}_{Guid.NewGuid():N}")
            : candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        const string portableInvalidCharacters = "<>:\"/\\|?*";
        var safe = new string(value
            .Select(character => char.IsControl(character) || portableInvalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "Animator" : safe;
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
