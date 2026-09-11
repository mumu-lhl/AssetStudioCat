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
    }

    public IReadOnlyList<BundleDecompressionMode> Modes { get; } = Enum.GetValues<BundleDecompressionMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskDirectoryEnabled))]
    public partial BundleDecompressionMode SelectedMode { get; set; }

    [ObservableProperty]
    public partial string DecompressionDirectory { get; set; }

    [ObservableProperty]
    public partial string CacheRoot { get; set; }

    [ObservableProperty]
    public partial int PreviewCacheMegabytes { get; set; }

    public bool IsDiskDirectoryEnabled => SelectedMode != BundleDecompressionMode.Memory;

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _settings.DecompressionMode = SelectedMode;
        _settings.DecompressionDirectory = DecompressionDirectory;
        _settings.CacheRoot = CacheRoot;
        _settings.PreviewCacheMegabytes = PreviewCacheMegabytes;
        await _store.SaveAsync(_settings, cancellationToken);
    }
}
