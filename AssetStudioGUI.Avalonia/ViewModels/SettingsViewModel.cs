using AssetStudio.AppCore.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _store;

    public SettingsViewModel(AppSettings settings, AppSettingsStore store)
    {
        _settings = settings;
        _store = store;
        SelectedMode = settings.DecompressionMode;
        DecompressionDirectory = settings.DecompressionDirectory ?? string.Empty;
        CacheRoot = settings.CacheRoot ?? string.Empty;
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
    }

    public IReadOnlyList<BundleDecompressionMode> Modes { get; } = Enum.GetValues<BundleDecompressionMode>();

    public IReadOnlyList<ConvertedImageFormat> ImageFormats { get; } = Enum.GetValues<ConvertedImageFormat>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskDirectoryEnabled))]
    public partial BundleDecompressionMode SelectedMode { get; set; }

    [ObservableProperty]
    public partial string DecompressionDirectory { get; set; }

    [ObservableProperty]
    public partial string CacheRoot { get; set; }

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

    public bool IsDiskDirectoryEnabled => SelectedMode != BundleDecompressionMode.Memory;

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _settings.DecompressionMode = SelectedMode;
        _settings.DecompressionDirectory = DecompressionDirectory;
        _settings.CacheRoot = CacheRoot;
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
        await _store.SaveAsync(_settings, cancellationToken);
    }
}
