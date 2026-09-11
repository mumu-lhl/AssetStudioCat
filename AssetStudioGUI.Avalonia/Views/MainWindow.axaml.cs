using Avalonia.Controls;
using Avalonia.Interactivity;
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
}
