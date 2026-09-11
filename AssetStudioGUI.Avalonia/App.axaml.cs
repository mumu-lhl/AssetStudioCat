using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetStudio.AppCore.Configuration;
using AssetStudioGUI.Avalonia.ViewModels;
using AssetStudioGUI.Avalonia.Views;

namespace AssetStudioGUI.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var directories = AppDirectories.Detect();
            var settingsStore = new AppSettingsStore(directories);
            var settings = settingsStore.LoadAsync().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(settingsStore)
            {
                DataContext = new MainViewModel(settings, directories),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
