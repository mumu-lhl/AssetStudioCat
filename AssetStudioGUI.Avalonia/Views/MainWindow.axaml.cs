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
}
