using Avalonia.Controls;
using Avalonia.Interactivity;
using AssetStudioGUI.Avalonia.ViewModels;

namespace AssetStudioGUI.Avalonia.Views;

public partial class CacheManagerWindow : Window
{
    public CacheManagerWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is CacheManagerViewModel viewModel) await viewModel.RefreshAsync();
        };
    }

    private async void Refresh(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CacheManagerViewModel viewModel) await viewModel.RefreshAsync();
    }

    private async void DeleteSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CacheManagerViewModel viewModel)
        {
            await viewModel.DeleteAsync(CacheList.SelectedItem as CacheIndexRowViewModel);
        }
    }

    private async void ClearAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CacheManagerViewModel viewModel) await viewModel.ClearAsync();
    }
}
