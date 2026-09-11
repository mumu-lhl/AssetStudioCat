using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Title => "AssetStudioCat";

    public ObservableCollection<AssetRowViewModel> Assets { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }
}

public sealed record AssetRowViewModel(
    string Name,
    string Container,
    string Type,
    long PathId,
    long Size);
