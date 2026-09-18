using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetStudio.AppCore.Configuration;
using AssetStudioGUI.Avalonia.Localization;
using AssetStudioGUI.Avalonia.ViewModels;

namespace AssetStudioGUI.Avalonia.Views;

public partial class SettingsWindow : Window
{
    private AppLocalizer _localizer = new();

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(AppSettings settings, AppSettingsStore store, AppLocalizer localizer)
        : this()
    {
        _localizer = localizer;
        DataContext = new SettingsViewModel(settings, store, localizer);
    }

    private async void BrowseDecompressionDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            var selected = await PickFolderAsync(_localizer["SelectDecompressionDirectory"]);
            if (selected is not null)
            {
                viewModel.DecompressionDirectory = selected;
            }
        }
    }

    private async void BrowseCacheRoot(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            var selected = await PickFolderAsync(_localizer["SelectPersistentCacheDirectory"]);
            if (selected is not null)
            {
                viewModel.CacheRoot = selected;
            }
        }
    }

    private async void BrowseAssemblyDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            var selected = await PickFolderAsync(_localizer["SelectAssemblyFolder"]);
            if (selected is not null)
            {
                viewModel.AssemblyDirectory = selected;
            }
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.SaveAsync();
            Close(true);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new Window
        {
            Title = _localizer["UnableToSaveSettings"],
            Width = 440,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new global::Avalonia.Thickness(20),
                Text = message,
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            },
        };
        await dialog.ShowDialog(this);
    }
}
