using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetStudio.AppCore.Configuration;
using AssetStudioGUI.Avalonia.Localization;
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
            var settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
            var localizer = new AppLocalizer(settings.Language);
            desktop.MainWindow = new MainWindow(settingsStore, localizer)
            {
                DataContext = new MainViewModel(settings, directories, settingsStore, localizer),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
