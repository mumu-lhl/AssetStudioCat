using System.Collections.ObjectModel;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Exporting;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Inspection;
using AssetStudio.AppCore.Loading;
using AssetStudio.AppCore.Preview;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 250;
    private readonly AppDirectories _directories;
    private DiskAssetIndex? _currentIndex;
    private TexturePreviewService? _previewService;
    private AssetInspectionService? _inspectionService;
    private AssetExportService? _exportService;
    private AnimatorExportService? _animatorExportService;
    private ConvertedAssetExportService? _convertedExportService;
    private BatchExportService? _batchExportService;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _dumpCancellation;
    private CancellationTokenSource? _exportCancellation;
    private readonly List<AssetRowViewModel> _selectedAssets = [];
    private IReadOnlyList<string> _sourcePaths = [];
    private bool _openedAsFileSelection;
    private int _pageOffset;

    public MainViewModel()
        : this(CreateDefaultSettings(), AppDirectories.Detect())
    {
    }

    public MainViewModel(AppSettings settings, AppDirectories directories)
    {
        Settings = settings;
        _directories = directories;
    }

    public string Title => "AssetStudioCat";

    public ObservableCollection<AssetRowViewModel> Assets { get; } = [];

    public ObservableCollection<string> AssetTypes { get; } = ["All types"];

    public ObservableCollection<AssetClassRowViewModel> AssetClasses { get; } = [];

    public ObservableCollection<SceneNodeViewModel> SceneRoots { get; } = [];

    public AppSettings Settings { get; }

    public bool HasSource => _sourcePaths.Count > 0;

    public bool CanGoPrevious => !IsBusy && _pageOffset > 0;

    public bool CanGoNext => !IsBusy && NextOffset is not null;

    public int? NextOffset { get; private set; }

    public string PageSummary => _currentIndex is null
        ? "No index open"
        : $"Rows {_pageOffset + 1}–{_pageOffset + Assets.Count}";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedType { get; set; } = "All types";

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

    [ObservableProperty]
    public partial bool IsPreviewBusy { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial string PreviewMessage { get; set; } = "Select a Texture2D asset to preview it.";

    [ObservableProperty]
    public partial string AssetInformation { get; set; } = "No asset selected.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAsset))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanExportAnimator))]
    public partial AssetRowViewModel? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string DumpText { get; set; } = "Select an asset, then choose Load dump.";

    [ObservableProperty]
    public partial bool IsDumpBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanExportAnimator))]
    public partial bool IsExportBusy { get; set; }

    [ObservableProperty]
    public partial bool IsBatchExportBusy { get; set; }

    [ObservableProperty]
    public partial bool IsHierarchyBusy { get; set; }

    public bool HasSelectedAsset => SelectedAsset is not null;

    public bool CanExport => HasSelectedAsset && !IsExportBusy;

    public bool CanExportAnimator => CanExport && SelectedAsset?.Type == "Animator";

    public bool HasBatchSelection => _selectedAssets.Count > 0;

    public int SelectedAssetCount => _selectedAssets.Count;

    public async Task OpenSourceAsync(string sourcePath, bool forceRebuild = false)
    {
        await OpenSourcesAsync([sourcePath], false, forceRebuild);
    }

    public async Task OpenFilesAsync(IReadOnlyList<string> sourceFiles, bool forceRebuild = false)
    {
        await OpenSourcesAsync(sourceFiles, true, forceRebuild);
    }

    private async Task OpenSourcesAsync(
        IReadOnlyList<string> sourcePaths,
        bool fileSelection,
        bool forceRebuild)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyNavigationChanged();
        ProgressValue = 0;
        StatusText = forceRebuild ? "Rebuilding asset index…" : "Checking asset index…";
        try
        {
            var settings = Settings.Normalize(_directories);
            var layout = new CacheLayout(settings);
            var builder = new AssetIndexBuilder(settings, layout);
            var progress = new Progress<int>(value => ProgressValue = value);
            var result = fileSelection
                ? await builder.OpenFilesAsync(sourcePaths, forceRebuild, progress)
                : await builder.OpenAsync(sourcePaths[0], forceRebuild, progress);
            _sourcePaths = sourcePaths.Select(Path.GetFullPath).ToArray();
            _openedAsFileSelection = fileSelection;
            _currentIndex = result.Index;
            _previewService = new TexturePreviewService(
                new AssetObjectLoader(settings, layout),
                new MemoryPreviewCache(settings.PreviewCacheMegabytes));
            _inspectionService = new AssetInspectionService(new AssetObjectLoader(settings, layout));
            _exportService = new AssetExportService(new AssetObjectLoader(settings, layout));
            _animatorExportService = new AnimatorExportService(new AssetObjectLoader(settings, layout), settings);
            _convertedExportService = new ConvertedAssetExportService(new AssetObjectLoader(settings, layout), settings);
            _batchExportService = new BatchExportService(_exportService, _convertedExportService, _animatorExportService);
            var typeCounts = await _currentIndex.GetTypeCountsAsync();
            AssetTypes.Clear();
            AssetClasses.Clear();
            AssetTypes.Add("All types");
            foreach (var typeName in typeCounts.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                AssetTypes.Add(typeName);
                AssetClasses.Add(new AssetClassRowViewModel(typeName, typeCounts[typeName]));
            }
            SceneRoots.Clear();
            SelectedType = "All types";
            _pageOffset = 0;
            await LoadPageAsync(0);
            StatusText = result.ReusedExistingIndex
                ? $"Opened cached index: {result.AssetCount:N0} assets"
                : $"Built streaming index: {result.AssetCount:N0} assets; {result.Decompression.Mode} decompression";
        }
        catch (Exception exception)
        {
            StatusText = $"Open failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            NotifyNavigationChanged();
        }
    }

    public Task RebuildAsync() => _sourcePaths.Count == 0
        ? Task.CompletedTask
        : OpenSourcesAsync(_sourcePaths, _openedAsFileSelection, true);

    public Task ApplyFilterAsync()
    {
        _pageOffset = 0;
        return LoadPageAsync(0);
    }

    public Task PreviousPageAsync() => LoadPageAsync(Math.Max(0, _pageOffset - PageSize));

    public Task NextPageAsync() => NextOffset is { } offset
        ? LoadPageAsync(offset)
        : Task.CompletedTask;

    public async Task SelectAssetAsync(AssetRowViewModel? row)
    {
        SelectedAsset = row;
        _dumpCancellation?.Cancel();
        IsDumpBusy = false;
        DumpText = row is null ? "Select an asset, then choose Load dump." : "Choose Load dump to inspect this object.";
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var cancellationToken = _previewCancellation.Token;
        IsPreviewBusy = false;

        var previousImage = PreviewImage;
        PreviewImage = null;
        previousImage?.Dispose();
        if (row is null)
        {
            PreviewMessage = "Select a Texture2D asset to preview it.";
            AssetInformation = "No asset selected.";
            return;
        }

        AssetInformation = $"{row.Name}\n{row.Type}\nPathID: {row.PathId}\nStored size: {row.Size:N0} bytes";
        if (!row.Type.Equals("Texture2D", StringComparison.Ordinal))
        {
            PreviewMessage = $"Preview for {row.Type} is not implemented yet.";
            return;
        }
        if (_previewService is null)
        {
            PreviewMessage = "The source index is not open.";
            return;
        }

        IsPreviewBusy = true;
        PreviewMessage = "Loading Texture2D from its source bundle…";
        try
        {
            var preview = await _previewService.LoadAsync(row.IndexEntry, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(preview.PngData, writable: false);
            PreviewImage = new Bitmap(stream);
            PreviewMessage = preview.FromCache ? "Decoded preview cache hit" : string.Empty;
            AssetInformation = preview.Information;
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this preview.
        }
        catch (Exception exception)
        {
            PreviewMessage = $"Preview failed: {exception.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsPreviewBusy = false;
            }
        }
    }

    public void SetSelectedAssets(IEnumerable<AssetRowViewModel> rows)
    {
        _selectedAssets.Clear();
        _selectedAssets.AddRange(rows);
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(SelectedAssetCount));
    }

    public async Task LoadHierarchyAsync()
    {
        if (_currentIndex is null || IsHierarchyBusy)
        {
            return;
        }

        IsHierarchyBusy = true;
        StatusText = "Building scene hierarchy from the disk index…";
        try
        {
            var roots = await new SceneHierarchyService().BuildAsync(_currentIndex);
            SceneRoots.Clear();
            foreach (var root in roots)
            {
                SceneRoots.Add(SceneNodeViewModel.FromNode(root));
            }
            StatusText = $"Scene hierarchy loaded: {roots.Count:N0} roots";
        }
        catch (Exception exception)
        {
            StatusText = $"Hierarchy failed: {exception.Message}";
        }
        finally
        {
            IsHierarchyBusy = false;
        }
    }

    public async Task SelectAssetClassAsync(AssetClassRowViewModel? assetClass)
    {
        if (assetClass is null)
        {
            return;
        }
        SelectedType = assetClass.TypeName;
        await ApplyFilterAsync();
    }

    public async Task LoadSelectedDumpAsync()
    {
        if (SelectedAsset is null || _inspectionService is null || IsDumpBusy)
        {
            return;
        }

        IsDumpBusy = true;
        DumpText = "Loading object dump…";
        _dumpCancellation?.Dispose();
        _dumpCancellation = new CancellationTokenSource();
        var cancellationToken = _dumpCancellation.Token;
        try
        {
            var result = await _inspectionService.LoadDumpAsync(SelectedAsset.IndexEntry, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DumpText = result.Text;
            StatusText = result.IsTruncated ? "Dump preview truncated to protect memory" : "Object dump loaded";
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this dump.
        }
        catch (Exception exception)
        {
            DumpText = $"Dump failed: {exception.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsDumpBusy = false;
            }
        }
    }

    public Task ExportSelectedRawAsync(string outputPath) =>
        ExportSelectedAsync(outputPath, dump: false);

    public Task ExportSelectedDumpAsync(string outputPath) =>
        ExportSelectedAsync(outputPath, dump: true);

    public async Task ExportSelectedAnimatorAsync(string outputDirectory)
    {
        if (SelectedAsset is null || _animatorExportService is null || !CanExportAnimator)
        {
            return;
        }

        IsExportBusy = true;
        StatusText = "Loading the selected AssetBundle and exporting Animator FBX…";
        try
        {
            var result = await _animatorExportService.ExportAsync(SelectedAsset.IndexEntry, outputDirectory);
            StatusText = $"Animator exported ({result.Files.Count} files)";
        }
        catch (Exception exception)
        {
            StatusText = $"Animator export failed: {exception.Message}";
        }
        finally
        {
            IsExportBusy = false;
        }
    }

    public async Task ExportSelectedConvertedAsync(string outputDirectory)
    {
        if (SelectedAsset is null || _convertedExportService is null || IsExportBusy)
        {
            return;
        }

        IsExportBusy = true;
        StatusText = $"Converting {SelectedAsset.Type}…";
        try
        {
            var result = await _convertedExportService.ExportAsync(SelectedAsset.IndexEntry, outputDirectory);
            StatusText = result.Note ?? $"Converted asset exported ({result.Files.Count} files)";
        }
        catch (Exception exception)
        {
            StatusText = $"Converted export failed: {exception.Message}";
        }
        finally
        {
            IsExportBusy = false;
        }
    }

    public async Task ExportBatchAsync(string outputDirectory, BatchExportMode mode, bool selectedOnly)
    {
        if (_batchExportService is null || _currentIndex is null || IsExportBusy)
        {
            return;
        }

        var selectedSnapshot = _selectedAssets.Select(row => row.IndexEntry).ToArray();
        if (selectedOnly && selectedSnapshot.Length == 0)
        {
            return;
        }

        _exportCancellation?.Dispose();
        _exportCancellation = new CancellationTokenSource();
        var cancellationToken = _exportCancellation.Token;
        var entries = selectedOnly
            ? BatchExportService.FromEntries(selectedSnapshot, cancellationToken)
            : _currentIndex.EnumerateAsync(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                SelectedType == "All types" ? null : SelectedType,
                cancellationToken);
        IsExportBusy = true;
        IsBatchExportBusy = true;
        StatusText = selectedOnly ? "Exporting selected assets…" : "Exporting filtered assets…";
        var progress = new Progress<BatchExportProgress>(value =>
        {
            StatusText = $"Exported {value.Completed}: {value.Succeeded} succeeded, {value.Failed} failed — {value.AssetName}";
        });
        try
        {
            var result = await _batchExportService.ExportAsync(
                entries,
                outputDirectory,
                mode,
                progress,
                cancellationToken);
            StatusText = result.Failed == 0
                ? $"Batch export complete: {result.Succeeded} succeeded"
                : $"Batch export complete: {result.Succeeded} succeeded, {result.Failed} failed; see {result.ErrorLogPath}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Batch export cancelled";
        }
        finally
        {
            IsBatchExportBusy = false;
            IsExportBusy = false;
        }
    }

    public void CancelExport() => _exportCancellation?.Cancel();

    private async Task ExportSelectedAsync(string outputPath, bool dump)
    {
        if (SelectedAsset is null || _exportService is null || IsExportBusy)
        {
            return;
        }

        IsExportBusy = true;
        StatusText = dump ? "Exporting complete dump…" : "Streaming raw asset to disk…";
        try
        {
            var result = dump
                ? await _exportService.ExportDumpAsync(SelectedAsset.IndexEntry, outputPath)
                : await _exportService.ExportRawAsync(SelectedAsset.IndexEntry, outputPath);
            StatusText = $"Exported {result.Files.Count} file{(result.Files.Count == 1 ? string.Empty : "s")}";
        }
        catch (Exception exception)
        {
            StatusText = $"Export failed: {exception.Message}";
        }
        finally
        {
            IsExportBusy = false;
        }
    }

    private async Task LoadPageAsync(int offset)
    {
        if (_currentIndex is null)
        {
            return;
        }

        var page = await _currentIndex.QueryAsync(new AssetIndexQuery(
            offset,
            PageSize,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            SelectedType == "All types" ? null : SelectedType));
        Assets.Clear();
        foreach (var entry in page.Items)
        {
            Assets.Add(new AssetRowViewModel(
                entry.Name,
                entry.Container ?? string.Empty,
                entry.TypeName,
                entry.PathId,
                entry.ByteSize,
                entry));
        }
        _pageOffset = page.Offset;
        NextOffset = page.NextOffset;
        OnPropertyChanged(nameof(PageSummary));
        NotifyNavigationChanged();
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(DecompressionSummary));
    }

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    public void Dispose()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _dumpCancellation?.Cancel();
        _dumpCancellation?.Dispose();
        _exportCancellation?.Cancel();
        _exportCancellation?.Dispose();
        PreviewImage?.Dispose();
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
    long Size,
    AssetIndexEntry IndexEntry);

public sealed record AssetClassRowViewModel(string TypeName, long Count);

public sealed record SceneNodeViewModel(
    string Name,
    string Details,
    IReadOnlyList<SceneNodeViewModel> Children)
{
    public static SceneNodeViewModel FromNode(SceneHierarchyNode node) => new(
        node.Name,
        $"PathID {node.GameObjectPathId}",
        node.Children.Select(FromNode).ToArray());
}
