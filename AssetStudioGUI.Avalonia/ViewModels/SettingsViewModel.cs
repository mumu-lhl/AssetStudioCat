using AssetStudio;
using AssetStudio.AppCore.Configuration;
using AssetStudioGUI.Avalonia.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _store;
    private readonly AppLocalizer _localizer;

    public SettingsViewModel(AppSettings settings, AppSettingsStore store, AppLocalizer localizer)
    {
        _settings = settings;
        _store = store;
        _localizer = localizer;
        SelectedMode = settings.DecompressionMode;
        DecompressionDirectory = settings.DecompressionDirectory ?? string.Empty;
        CacheRoot = settings.CacheRoot ?? string.Empty;
        CustomUnityVersion = settings.CustomUnityVersion ?? string.Empty;
        PreviewCacheMegabytes = settings.PreviewCacheMegabytes;
        SelectedImageFormat = settings.ConvertedImageFormat;
        ExportSpriteWithMask = settings.ExportSpriteWithMask;
        ConvertAudioToWav = settings.ConvertAudioToWav;
        FbxExportAnimations = settings.FbxExportAnimations;
        FbxExportSkins = settings.FbxExportSkins;
        FbxExportBlendShapes = settings.FbxExportBlendShapes;
        FbxExportAllNodes = settings.FbxExportAllNodes;
        FbxEulerFilter = settings.FbxEulerFilter;
        FbxAscii = settings.FbxAscii;
        FbxScaleFactor = settings.FbxScaleFactor;
        SelectedGltfFormat = settings.GltfFormat;
        GltfExportAnimations = settings.GltfExportAnimations;
        GltfExportSkins = settings.GltfExportSkins;
        GltfExportBlendShapes = settings.GltfExportBlendShapes;
        GltfScaleFactor = settings.GltfScaleFactor;
        RestoreLastSource = settings.RestoreLastSource;
        SelectedLanguage = AppLocalizer.NormalizeSettingLanguage(settings.Language);
    }

    public AppLocalizer L => _localizer;

    public IReadOnlyList<LanguageOption> Languages => _localizer.Languages;

    public IReadOnlyList<BundleDecompressionMode> Modes { get; } = Enum.GetValues<BundleDecompressionMode>();

    public IReadOnlyList<ConvertedImageFormat> ImageFormats { get; } = Enum.GetValues<ConvertedImageFormat>();

    public IReadOnlyList<Gltf.Format> GltfFormats { get; } = Enum.GetValues<Gltf.Format>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskDirectoryEnabled))]
    public partial BundleDecompressionMode SelectedMode { get; set; }

    [ObservableProperty]
    public partial string DecompressionDirectory { get; set; }

    [ObservableProperty]
    public partial string CacheRoot { get; set; }

    [ObservableProperty]
    public partial string CustomUnityVersion { get; set; }

    [ObservableProperty]
    public partial int PreviewCacheMegabytes { get; set; }

    [ObservableProperty]
    public partial ConvertedImageFormat SelectedImageFormat { get; set; }

    [ObservableProperty]
    public partial bool ExportSpriteWithMask { get; set; }

    [ObservableProperty]
    public partial bool ConvertAudioToWav { get; set; }

    [ObservableProperty]
    public partial bool FbxExportAnimations { get; set; }

    [ObservableProperty]
    public partial bool FbxExportSkins { get; set; }

    [ObservableProperty]
    public partial bool FbxExportBlendShapes { get; set; }

    [ObservableProperty]
    public partial bool FbxExportAllNodes { get; set; }

    [ObservableProperty]
    public partial bool FbxEulerFilter { get; set; }

    [ObservableProperty]
    public partial bool FbxAscii { get; set; }

    [ObservableProperty]
    public partial double FbxScaleFactor { get; set; }

    [ObservableProperty]
    public partial Gltf.Format SelectedGltfFormat { get; set; }

    [ObservableProperty]
    public partial bool GltfExportAnimations { get; set; }

    [ObservableProperty]
    public partial bool GltfExportSkins { get; set; }

    [ObservableProperty]
    public partial bool GltfExportBlendShapes { get; set; }

    [ObservableProperty]
    public partial double GltfScaleFactor { get; set; }

    [ObservableProperty]
    public partial bool RestoreLastSource { get; set; }

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; }

    public bool IsDiskDirectoryEnabled => SelectedMode != BundleDecompressionMode.Memory;

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _settings.DecompressionMode = SelectedMode;
        _settings.DecompressionDirectory = DecompressionDirectory;
        _settings.CacheRoot = CacheRoot;
        _settings.CustomUnityVersion = CustomUnityVersion;
        _settings.PreviewCacheMegabytes = PreviewCacheMegabytes;
        _settings.ConvertedImageFormat = SelectedImageFormat;
        _settings.ExportSpriteWithMask = ExportSpriteWithMask;
        _settings.ConvertAudioToWav = ConvertAudioToWav;
        _settings.FbxExportAnimations = FbxExportAnimations;
        _settings.FbxExportSkins = FbxExportSkins;
        _settings.FbxExportBlendShapes = FbxExportBlendShapes;
        _settings.FbxExportAllNodes = FbxExportAllNodes;
        _settings.FbxEulerFilter = FbxEulerFilter;
        _settings.FbxAscii = FbxAscii;
        _settings.FbxScaleFactor = FbxScaleFactor;
        _settings.GltfFormat = SelectedGltfFormat;
        _settings.GltfExportAnimations = GltfExportAnimations;
        _settings.GltfExportSkins = GltfExportSkins;
        _settings.GltfExportBlendShapes = GltfExportBlendShapes;
        _settings.GltfScaleFactor = GltfScaleFactor;
        _settings.RestoreLastSource = RestoreLastSource;
        _settings.Language = AppLocalizer.NormalizeSettingLanguage(SelectedLanguage);
        await _store.SaveAsync(_settings, cancellationToken);
        _localizer.Language = _settings.Language;
    }
}
