using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly AppSettingsStore? _settingsStore;
    private DiskAssetIndex? _currentIndex;
    private AssetPreviewService? _previewService;
    private AssetInspectionService? _inspectionService;
    private AssetExportService? _exportService;
    private AnimatorExportService? _animatorExportService;
    private GameObjectExportService? _gameObjectExportService;
    private ConvertedAssetExportService? _convertedExportService;
    private BatchExportService? _batchExportService;
    private BundleExtractionService? _bundleExtractionService;
    private readonly AssetListExportService _assetListExportService = new();
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _dumpCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _pageCancellation;
    private readonly List<AssetRowViewModel> _selectedAssets = [];
    private IReadOnlyList<string> _sourcePaths = [];
    private bool _openedAsFileSelection;
    private int _pageOffset;
    private int _pageGeneration;

    public MainViewModel()
        : this(CreateDefaultSettings(), AppDirectories.Detect(), null)
    {
    }

    public MainViewModel(AppSettings settings, AppDirectories directories, AppSettingsStore? settingsStore = null)
    {
        Settings = settings;
        _directories = directories;
        _settingsStore = settingsStore;
    }

    public string Title => "AssetStudioCat";

    public ObservableCollection<AssetRowViewModel> Assets { get; } = [];

    public ObservableCollection<string> AssetTypes { get; } = ["All types"];

    public ObservableCollection<AssetClassRowViewModel> AssetClasses { get; } = [];

    public ObservableCollection<SceneNodeViewModel> SceneRoots { get; } = [];

    public IReadOnlyList<AssetSortField> SortFields { get; } = Enum.GetValues<AssetSortField>();

    public AppSettings Settings { get; }

    public bool HasSource => _sourcePaths.Count > 0;

    public bool CanOpenRecent => Settings.RecentSources.Count > 0;

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

    [ObservableProperty]
    public partial AssetSortField SelectedSortField { get; set; } = AssetSortField.IndexOrder;

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

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
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewText))]
    public partial string? PreviewText { get; set; }

    [ObservableProperty]
    public partial string PreviewMessage { get; set; } = "Select a Texture2D asset to preview it.";

    [ObservableProperty]
    public partial string AssetInformation { get; set; } = "No asset selected.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAsset))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanExportAnimator))]
    [NotifyPropertyChangedFor(nameof(CanPlayAudio))]
    public partial AssetRowViewModel? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string DumpText { get; set; } = "Select an asset, then choose Load dump.";

    [ObservableProperty]
    public partial bool IsDumpBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanExportAnimator))]
    [NotifyPropertyChangedFor(nameof(CanExportSceneModel))]
    [NotifyPropertyChangedFor(nameof(CanPlayAudio))]
    public partial bool IsExportBusy { get; set; }

    [ObservableProperty]
    public partial bool IsBatchExportBusy { get; set; }

    [ObservableProperty]
    public partial bool IsExtractionBusy { get; set; }

    [ObservableProperty]
    public partial bool IsHierarchyBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportSceneModel))]
    public partial SceneNodeViewModel? SelectedSceneNode { get; set; }

    public bool HasSelectedAsset => SelectedAsset is not null;

    public bool HasPreviewImage => PreviewImage is not null;

    public bool HasPreviewText => !string.IsNullOrEmpty(PreviewText);

    public bool CanExport => HasSelectedAsset && !IsExportBusy;

    public bool CanExportAnimator => CanExport && SelectedAsset?.Type == "Animator";

    public bool CanExportSceneModel => SelectedSceneNode is not null && !IsExportBusy;

    public bool CanPlayAudio => SelectedAsset?.Type == "AudioClip" && !IsExportBusy;

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
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
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
                ? await builder.OpenFilesAsync(sourcePaths, forceRebuild, progress, cancellationToken)
                : await builder.OpenAsync(sourcePaths[0], forceRebuild, progress, cancellationToken);
            _sourcePaths = sourcePaths.Select(Path.GetFullPath).ToArray();
            _openedAsFileSelection = fileSelection;
            _currentIndex = result.Index;
            _previewService = new AssetPreviewService(
                new AssetObjectLoader(settings, layout),
                new MemoryPreviewCache(settings.PreviewCacheMegabytes),
                settings);
            _inspectionService = new AssetInspectionService(new AssetObjectLoader(settings, layout));
            _exportService = new AssetExportService(new AssetObjectLoader(settings, layout));
            _animatorExportService = new AnimatorExportService(new AssetObjectLoader(settings, layout), settings);
            _gameObjectExportService = new GameObjectExportService(new AssetObjectLoader(settings, layout), settings);
            _convertedExportService = new ConvertedAssetExportService(new AssetObjectLoader(settings, layout), settings);
            _batchExportService = new BatchExportService(_exportService, _convertedExportService, _animatorExportService);
            _bundleExtractionService = new BundleExtractionService(settings, layout);
            var typeCounts = await _currentIndex.GetTypeCountsAsync(cancellationToken);
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
            await LoadPageAsync(0, cancellationToken);
            await RememberSourceAsync(_sourcePaths, fileSelection, cancellationToken);
            StatusText = result.ReusedExistingIndex
                ? $"Opened cached index: {result.AssetCount:N0} assets"
                : $"Built streaming index: {result.AssetCount:N0} assets; {result.Decompression.Mode} decompression";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Loading cancelled";
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

    public void CancelLoad() => _loadCancellation?.Cancel();

    public async Task OpenMostRecentAsync()
    {
        var recent = Settings.RecentSources.FirstOrDefault();
        if (recent is not null)
        {
            await OpenSourcesAsync(recent.Paths, recent.IsFileSelection, false);
        }
    }

    public async Task RestoreLastSourceAsync()
    {
        if (!Settings.RestoreLastSource || Settings.RecentSources.FirstOrDefault() is not { } recent)
        {
            return;
        }
        if (recent.Paths.All(path => recent.IsFileSelection ? File.Exists(path) : Directory.Exists(path)))
        {
            await OpenSourcesAsync(recent.Paths, recent.IsFileSelection, false);
        }
        else
        {
            StatusText = "The most recent source is no longer available";
        }
    }

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
        PreviewText = null;
        previousImage?.Dispose();
        if (row is null)
        {
            PreviewMessage = "Select a Texture2D asset to preview it.";
            AssetInformation = "No asset selected.";
            return;
        }

        AssetInformation = $"{row.Name}\n{row.Type}\nPathID: {row.PathId}\nStored size: {row.Size:N0} bytes";
        if (_previewService is null)
        {
            PreviewMessage = "The source index is not open.";
            return;
        }
        if (!_previewService.Supports(row.Type))
        {
            PreviewMessage = $"Preview for {row.Type} is not implemented yet.";
            return;
        }

        IsPreviewBusy = true;
        PreviewMessage = $"Loading {row.Type} from its source bundle…";
        try
        {
            var preview = await _previewService.LoadAsync(row.IndexEntry, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (preview.PngData is not null)
            {
                using var stream = new MemoryStream(preview.PngData, writable: false);
                PreviewImage = new Bitmap(stream);
            }
            PreviewText = preview.Text;
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

    public void SelectSceneNode(SceneNodeViewModel? node) => SelectedSceneNode = node;

    public async Task ExportSelectedSceneModelAsync(string outputDirectory)
    {
        if (SelectedSceneNode is null || _gameObjectExportService is null || IsExportBusy)
        {
            return;
        }
        IsExportBusy = true;
        StatusText = $"Exporting scene model {SelectedSceneNode.Name}…";
        try
        {
            var result = await _gameObjectExportService.ExportAsync(SelectedSceneNode.TransformEntry, outputDirectory);
            StatusText = $"Scene model exported ({result.Files.Count} files)";
        }
        catch (Exception exception)
        {
            StatusText = $"Scene model export failed: {exception.Message}";
        }
        finally
        {
            IsExportBusy = false;
        }
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

    public async Task PlaySelectedAudioAsync()
    {
        if (SelectedAsset is null || _convertedExportService is null || !CanPlayAudio)
        {
            return;
        }
        IsExportBusy = true;
        StatusText = "Preparing AudioClip for the system player…";
        try
        {
            var settings = Settings.Normalize(_directories);
            var playbackDirectory = Path.Combine(new CacheLayout(settings).Previews, "audio-playback");
            var result = await _convertedExportService.ExportAsync(SelectedAsset.IndexEntry, playbackDirectory);
            var file = result.Files.FirstOrDefault() ?? throw new InvalidDataException("Audio export produced no playable file.");
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open", file) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", file) { UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            }
            StatusText = result.Note ?? "Opened AudioClip in the system player";
        }
        catch (Exception exception)
        {
            StatusText = $"Audio playback failed: {exception.Message}";
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

    public async Task ExtractOpenedSourcesAsync(string outputDirectory)
    {
        if (_bundleExtractionService is null || _sourcePaths.Count == 0 || IsExtractionBusy)
        {
            return;
        }
        _exportCancellation?.Dispose();
        _exportCancellation = new CancellationTokenSource();
        var token = _exportCancellation.Token;
        IsExtractionBusy = true;
        StatusText = "Extracting Bundle files…";
        try
        {
            var progress = new Progress<BundleExtractionProgress>(value =>
            {
                StatusText = $"Extracted {value.ExtractedFiles:N0} files — {value.CompletedSources:N0}/{value.TotalSources:N0}: {Path.GetFileName(value.SourcePath)}";
            });
            var count = await _bundleExtractionService.ExtractAsync(_sourcePaths, outputDirectory, progress, token);
            StatusText = $"Bundle extraction complete: {count:N0} files";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Bundle extraction cancelled";
        }
        catch (Exception exception)
        {
            StatusText = $"Bundle extraction failed: {exception.Message}";
        }
        finally
        {
            IsExtractionBusy = false;
        }
    }

    public async Task ExportAssetListAsync(string outputPath, AssetListExportFormat format)
    {
        if (_currentIndex is null || IsExportBusy)
        {
            return;
        }
        _exportCancellation?.Dispose();
        _exportCancellation = new CancellationTokenSource();
        IsExportBusy = true;
        StatusText = "Writing filtered asset list…";
        try
        {
            var count = await _assetListExportService.ExportAsync(
                _currentIndex,
                outputPath,
                format,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                SelectedType == "All types" ? null : SelectedType,
                _exportCancellation.Token);
            StatusText = $"Asset list exported: {count:N0} rows";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Asset list export cancelled";
        }
        catch (Exception exception)
        {
            StatusText = $"Asset list export failed: {exception.Message}";
        }
        finally
        {
            IsExportBusy = false;
        }
    }

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

    private async Task LoadPageAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (_currentIndex is null)
        {
            return;
        }

        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _pageCancellation.Token;
        var generation = ++_pageGeneration;
        AssetIndexPage page;
        try
        {
            page = await _currentIndex.QueryAsync(new AssetIndexQuery(
                offset,
                PageSize,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                SelectedType == "All types" ? null : SelectedType,
                SelectedSortField,
                SortDescending), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        if (generation != _pageGeneration)
        {
            return;
        }
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

    private async Task RememberSourceAsync(
        IReadOnlyList<string> paths,
        bool fileSelection,
        CancellationToken cancellationToken)
    {
        if (_settingsStore is null)
        {
            return;
        }
        var normalized = paths.Select(Path.GetFullPath).ToArray();
        Settings.RecentSources.RemoveAll(item =>
            item.IsFileSelection == fileSelection
            && item.Paths.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase));
        Settings.RecentSources.Insert(0, new RecentSource(normalized, fileSelection, DateTimeOffset.UtcNow));
        if (Settings.RecentSources.Count > 10)
        {
            Settings.RecentSources.RemoveRange(10, Settings.RecentSources.Count - 10);
        }
        await _settingsStore.SaveAsync(Settings, cancellationToken);
        OnPropertyChanged(nameof(CanOpenRecent));
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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
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
    AssetIndexEntry TransformEntry,
    IReadOnlyList<SceneNodeViewModel> Children)
{
    public static SceneNodeViewModel FromNode(SceneHierarchyNode node) => new(
        node.Name,
        $"PathID {node.GameObjectPathId}",
        node.TransformEntry,
        node.Children.Select(FromNode).ToArray());
}
