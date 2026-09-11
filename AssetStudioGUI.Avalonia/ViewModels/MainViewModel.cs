using System.Collections.ObjectModel;
using AssetStudio.AppCore.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
        : this(CreateDefaultSettings())
    {
    }

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
    }

    public string Title => "AssetStudioCat";

    public ObservableCollection<AssetRowViewModel> Assets { get; } = [];

    public AppSettings Settings { get; }

    public string DecompressionSummary => Settings.DecompressionMode switch
    {
        BundleDecompressionMode.Memory => "Bundle decompression: memory",
        BundleDecompressionMode.Disk => $"Bundle decompression: disk ({Settings.DecompressionDirectory})",
        _ => "Bundle decompression: automatic",
    };

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(DecompressionSummary));
    }

    private static AppSettings CreateDefaultSettings()
    {
        var directories = AppDirectories.Detect();
        return new AppSettings().Normalize(directories);
    }
}

public sealed record AssetRowViewModel(
    string Name,
    string Container,
    string Type,
    long PathId,
    long Size);
