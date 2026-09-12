using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetStudio.AppCore.Configuration;
using AssetStudioGUI.Avalonia.ViewModels;

namespace AssetStudioGUI.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly AppSettingsStore? _settingsStore;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AppSettingsStore settingsStore)
        : this()
    {
        _settingsStore = settingsStore;
    }

    private async void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_settingsStore is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new SettingsWindow(viewModel.Settings, _settingsStore);
        var saved = await window.ShowDialog<bool>(this);
        if (saved)
        {
            viewModel.NotifySettingsChanged();
            viewModel.StatusText = "Settings saved";
        }
    }

    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open AssetBundle directory",
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.OpenSourceAsync(path);
        }
    }

    private async void RebuildIndex(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.RebuildAsync();
        }
    }

    private async void FilterKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel viewModel)
        {
            await viewModel.ApplyFilterAsync();
        }
    }

    private async void PreviousPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.PreviousPageAsync();
        }
    }

    private async void NextPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.NextPageAsync();
        }
    }

    private async void AssetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is ListBox listBox)
        {
            await viewModel.SelectAssetAsync(listBox.SelectedItem as AssetRowViewModel);
        }
    }

    private async void LoadDump(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadSelectedDumpAsync();
        }
    }

    private async void ExportRaw(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedAsset: { } selected } viewModel)
        {
            return;
        }

        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export raw asset",
            SuggestedFileName = MakeSafeFileName(selected.Name) + ".dat",
            DefaultExtension = "dat",
            FileTypeChoices = [new FilePickerFileType("Raw asset") { Patterns = ["*.dat"] }],
        });
        if (output?.TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedRawAsync(path);
        }
    }

    private async void ExportDump(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedAsset: { } selected } viewModel)
        {
            return;
        }

        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export complete object dump",
            SuggestedFileName = MakeSafeFileName(selected.Name) + ".txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text dump") { Patterns = ["*.txt"] }],
        });
        if (output?.TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedDumpAsync(path);
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "asset" : safe;
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
