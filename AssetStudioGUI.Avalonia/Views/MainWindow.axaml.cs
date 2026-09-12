using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Exporting;
using AssetStudioGUI.Avalonia.Localization;
using AssetStudioGUI.Avalonia.ViewModels;

namespace AssetStudioGUI.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly AppSettingsStore? _settingsStore;
    private AppLocalizer _localizer = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AppSettingsStore settingsStore, AppLocalizer localizer)
        : this()
    {
        _settingsStore = settingsStore;
        _localizer = localizer;
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel viewModel) await viewModel.RestoreLastSourceAsync();
        };
    }

    private async void OpenCacheManager(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        var layout = new AssetStudio.AppCore.Caching.CacheLayout(viewModel.Settings.Normalize(AppDirectories.Detect()));
        var window = new CacheManagerWindow
        {
            DataContext = new CacheManagerViewModel(new AssetStudio.AppCore.Caching.CacheCatalog(layout), _localizer),
        };
        await window.ShowDialog(this);
    }

    private async void OpenMostRecent(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) await viewModel.OpenMostRecentAsync();
    }

    private void CancelLoad(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.CancelLoad();
    }

    private async void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_settingsStore is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new SettingsWindow(viewModel.Settings, _settingsStore, _localizer);
        var saved = await window.ShowDialog<bool>(this);
        if (saved)
        {
            viewModel.NotifySettingsChanged();
            viewModel.StatusText = _localizer["SettingsSaved"];
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
            Title = _localizer["OpenAssetBundleDirectory"],
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.OpenSourceAsync(path);
        }
    }

    private async void OpenFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _localizer["OpenAssetFiles"],
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(_localizer["UnityAssets"]) { Patterns = ["*"] }],
        });
        var paths = files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToArray();
        if (paths.Length > 0)
        {
            await viewModel.OpenFilesAsync(paths);
        }
    }

    private async void RebuildIndex(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.RebuildAsync();
        }
    }

    private async void ExtractOpenedSources(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localizer["ChooseExtractionDirectory"],
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.ExtractOpenedSourcesAsync(path);
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
            viewModel.SetSelectedAssets(listBox.SelectedItems?.OfType<AssetRowViewModel>() ?? []);
            var active = e.AddedItems.OfType<AssetRowViewModel>().LastOrDefault()
                ?? listBox.SelectedItems?.OfType<AssetRowViewModel>().LastOrDefault();
            await viewModel.SelectAssetAsync(active);
        }
    }

    private async void TypeFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } viewModel)
        {
            await viewModel.ApplyFilterAsync();
        }
    }

    private async void SortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } viewModel)
        {
            await viewModel.ApplyFilterAsync();
        }
    }

    private async void SortDirectionChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } viewModel)
        {
            await viewModel.ApplyFilterAsync();
        }
    }

    private async void LoadDump(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadSelectedDumpAsync();
        }
    }

    private async void PlayAudio(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) await viewModel.PlaySelectedAudioAsync();
    }

    private async void LoadHierarchy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadHierarchyAsync();
        }
    }

    private async void AssetClassSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is ListBox listBox)
        {
            await viewModel.SelectAssetClassAsync(listBox.SelectedItem as AssetClassRowViewModel);
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
            Title = _localizer["ExportRawAsset"],
            SuggestedFileName = MakeSafeFileName(selected.Name) + ".dat",
            DefaultExtension = "dat",
            FileTypeChoices = [new FilePickerFileType(_localizer["RawAsset"]) { Patterns = ["*.dat"] }],
        });
        if (output?.TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedRawAsync(path);
        }
    }

    private async Task ExportAssetList(AssetListExportFormat format)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var extension = format == AssetListExportFormat.Csv ? "csv" : "json";
        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _localizer.Format("ExportAssetList", extension.ToUpperInvariant()),
            SuggestedFileName = $"asset-list.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType($"{extension.ToUpperInvariant()} {_localizer["FileName"]}") { Patterns = [$"*.{extension}"] }],
        });
        if (output?.TryGetLocalPath() is { } path) await viewModel.ExportAssetListAsync(path, format);
    }

    private async void ExportAssetListCsv(object? sender, RoutedEventArgs e) => await ExportAssetList(AssetListExportFormat.Csv);

    private async void ExportAssetListJson(object? sender, RoutedEventArgs e) => await ExportAssetList(AssetListExportFormat.Json);

    private async void ExportConverted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localizer["ChooseConvertedDirectory"],
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedConvertedAsync(path);
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
            Title = _localizer["ExportCompleteDump"],
            SuggestedFileName = MakeSafeFileName(selected.Name) + ".txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType(_localizer["TextDump"]) { Patterns = ["*.txt"] }],
        });
        if (output?.TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedDumpAsync(path);
        }
    }

    private async void ExportAnimator(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localizer["ChooseAnimatorDirectory"],
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedAnimatorAsync(path);
        }
    }

    private async void ExportSceneModel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localizer["ChooseSceneDirectory"],
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.ExportSelectedSceneModelAsync(path);
        }
    }

    private void SceneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is TreeView treeView)
        {
            viewModel.SelectSceneNode(treeView.SelectedItem as SceneNodeViewModel);
        }
    }

    private async void ContainerTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is TreeView treeView)
        {
            await viewModel.SelectContainerNodeAsync(treeView.SelectedItem as ContainerTreeNodeViewModel);
        }
    }

    private async void ClearContainerFilter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ClearContainerFilterAsync();
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "asset" : safe;
    }

    private async Task BatchExport(BatchExportMode mode, bool selectedOnly)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localizer.Format("ChooseBatchDirectory", mode.ToString().ToLowerInvariant()),
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.ExportBatchAsync(path, mode, selectedOnly);
        }
    }

    private async void BatchConvertedSelected(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Converted, true);
    private async void BatchConvertedFiltered(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Converted, false);
    private async void BatchRawSelected(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Raw, true);
    private async void BatchRawFiltered(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Raw, false);
    private async void BatchDumpSelected(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Dump, true);
    private async void BatchDumpFiltered(object? sender, RoutedEventArgs e) => await BatchExport(BatchExportMode.Dump, false);

    private void CancelExport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelExport();
        }
    }

    private void CycleMeshWireframe(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.CycleMeshWireframeMode();
    }

    private void CycleMeshShade(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.CycleMeshShadeMode();
    }

    private void ToggleMeshNormals(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.ToggleMeshNormals();
    }

    private void ResetMeshCamera(object? sender, RoutedEventArgs e)
    {
        var viewport = this.FindControl<Controls.MeshViewportControl>("MeshViewport");
        viewport?.ResetCamera();
    }

    private bool _isResizingColumn;
    private int _resizingColumnIndex;
    private double _resizeStartX;
    private double _resizeStartWidth;

    private void ColumnSplitter_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tagStr && int.TryParse(tagStr, out var colIdx))
        {
            if (DataContext is not MainViewModel viewModel) return;

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            _isResizingColumn = true;
            _resizingColumnIndex = colIdx;
            _resizeStartX = point.Position.X;
            _resizeStartWidth = colIdx switch
            {
                0 => viewModel.ColumnWidthName.Value,
                1 => viewModel.ColumnWidthContainer.Value,
                2 => viewModel.ColumnWidthType.Value,
                3 => viewModel.ColumnWidthPathId.Value,
                4 => viewModel.ColumnWidthSize.Value,
                _ => 100
            };
            e.Pointer.Capture(border);
            e.Handled = true;
        }
    }

    private void ColumnSplitter_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingColumn) return;
        if (DataContext is not MainViewModel viewModel) return;

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _resizeStartX;
        var minWidth = _resizingColumnIndex switch
        {
            0 or 1 => 60.0,
            _ => 45.0
        };
        var newWidth = Math.Max(minWidth, _resizeStartWidth + delta);

        switch (_resizingColumnIndex)
        {
            case 0: viewModel.ColumnWidthName = new GridLength(newWidth); break;
            case 1: viewModel.ColumnWidthContainer = new GridLength(newWidth); break;
            case 2: viewModel.ColumnWidthType = new GridLength(newWidth); break;
            case 3: viewModel.ColumnWidthPathId = new GridLength(newWidth); break;
            case 4: viewModel.ColumnWidthSize = new GridLength(newWidth); break;
        }
        e.Handled = true;
    }

    private void ColumnSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizingColumn)
        {
            _isResizingColumn = false;
            if (sender is Control control)
            {
                e.Pointer.Capture(null);
            }
            e.Handled = true;
        }
    }

    private void ColumnSplitter_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isResizingColumn = false;
    }

    private void ColumnSplitter_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tagStr && int.TryParse(tagStr, out var colIdx))
        {
            if (DataContext is not MainViewModel viewModel) return;
            switch (colIdx)
            {
                case 0: viewModel.ColumnWidthName = new GridLength(240); break;
                case 1: viewModel.ColumnWidthContainer = new GridLength(200); break;
                case 2: viewModel.ColumnWidthType = new GridLength(110); break;
                case 3: viewModel.ColumnWidthPathId = new GridLength(100); break;
                case 4: viewModel.ColumnWidthSize = new GridLength(100); break;
            }
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
