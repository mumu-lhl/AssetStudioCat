using System.Collections.ObjectModel;
using System.Diagnostics;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Exporting;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Inspection;
using AssetStudio.AppCore.Loading;
using AssetStudio.AppCore.Preview;
using AssetStudioGUI.Avalonia.Localization;
using AssetStudioGUI.Avalonia.Services;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetStudioGUI.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 250;
    private readonly AppDirectories _directories;
    private readonly AppSettingsStore? _settingsStore;
    private readonly AppLocalizer _localizer;
    private string _allTypesLabel;
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
    private IReadOnlyList<ContainerHierarchyNode>? _containerHierarchyRoots;
    private IReadOnlyList<SceneHierarchyNode>? _sceneHierarchyRoots;
    private IReadOnlyList<string> _sourcePaths = [];
    private readonly AssetStudioHttpServer? _httpServer;
    private bool _openedAsFileSelection;
    private int _pageOffset;
    private int _pageGeneration;
    private long _totalAssetCount;

    public DiskAssetIndex? CurrentIndex => _currentIndex;
    public AssetPreviewService? PreviewService => _previewService;
    public ConvertedAssetExportService? ConvertedExportService => _convertedExportService;
    public AssetExportService? ExportService => _exportService;
    public GameObjectExportService? GameObjectExportService => _gameObjectExportService;
    public long TotalAssetCount => _totalAssetCount;

    public MainViewModel()
        : this(CreateDefaultSettings(), AppDirectories.Detect())
    {
    }

    public MainViewModel(
        AppSettings settings,
        AppDirectories directories,
        AppSettingsStore? settingsStore = null,
        AppLocalizer? localizer = null)
    {
        Settings = settings;
        _directories = directories;
        _settingsStore = settingsStore;
        _localizer = localizer ?? new AppLocalizer(settings.Language);
        _allTypesLabel = _localizer["AllTypes"];
        AssetTypes.Add(_allTypesLabel);
        SelectedType = _allTypesLabel;
        StatusText = _localizer["Ready"];
        PreviewMessage = _localizer["SelectTexturePreview"];
        AssetInformation = _localizer["NoAssetSelected"];
        DumpText = _localizer["SelectAssetLoadDump"];
        _localizer.PropertyChanged += LocalizerChanged;

        if (settings.EnableHttpApi)
        {
            _httpServer = new AssetStudioHttpServer(this, directories, settings.HttpApiPort);
            _httpServer.Start();
        }
    }

    public AppLocalizer L => _localizer;

    public string Title => "AssetStudioCat";

    public FastObservableCollection<AssetRowViewModel> Assets { get; } = [];

    public ObservableCollection<string> AssetTypes { get; } = [];

    public ObservableCollection<AssetClassRowViewModel> AssetClasses { get; } = [];

    public FastObservableCollection<SceneNodeViewModel> SceneRoots { get; } = [];

    public FastObservableCollection<ContainerTreeNodeViewModel> ContainerTreeRoots { get; } = [];

    [ObservableProperty]
    private int _selectedLeftTabIndex;

    public bool IsContainerTabVisible => SelectedLeftTabIndex == 0;
    public bool IsSceneTabVisible => SelectedLeftTabIndex == 1;
    public bool IsAssetClassesTabVisible => SelectedLeftTabIndex == 2;

    partial void OnSelectedLeftTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsContainerTabVisible));
        OnPropertyChanged(nameof(IsSceneTabVisible));
        OnPropertyChanged(nameof(IsAssetClassesTabVisible));
    }

    public IReadOnlyList<AssetSortField> SortFields { get; } = Enum.GetValues<AssetSortField>();

    public AppSettings Settings { get; }

    public bool HasSource => _sourcePaths.Count > 0;

    public string? SourceSummary => _sourcePaths.Count > 0 ? string.Join("; ", _sourcePaths) : null;

    public bool CanOpenRecent => Settings.RecentSources.Count > 0;

    public bool CanGoPrevious => !IsBusy && _pageOffset > 0;

    public bool CanGoNext => !IsBusy && NextOffset is not null;

    public int? NextOffset { get; private set; }

    public string PageSummary => _currentIndex is null
        ? T("NoIndexOpen")
        : T("Rows", _pageOffset + 1, _pageOffset + Assets.Count);

    public string ContainerTreeSummary => _currentIndex is null
        ? T("NoIndexOpen")
        : T("ContainerTreeSummary", _totalAssetCount);

    [ObservableProperty]
    public partial bool IsContainerTreeBusy { get; set; }

    [ObservableProperty]
    public partial ContainerTreeNodeViewModel? SelectedContainerNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContainerFilter))]
    public partial string? SelectedContainerPath { get; set; }

    [ObservableProperty]
    public partial string? SelectedContainerTitle { get; set; }

    [ObservableProperty]
    public partial bool SelectedContainerExact { get; set; }

    public bool HasContainerFilter => !string.IsNullOrEmpty(SelectedContainerPath);

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(ActiveSearchText))
        {
            ActiveSearchText = string.Empty;
            if (_currentIndex is not null && !IsBusy)
            {
                _ = ApplyFilterAsync();
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlatViewMode))]
    [NotifyPropertyChangedFor(nameof(IsDirectoryViewMode))]
    public partial string ActiveSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlatViewMode))]
    [NotifyPropertyChangedFor(nameof(IsDirectoryViewMode))]
    public partial string SelectedType { get; set; } = string.Empty;
 
    partial void OnSelectedTypeChanged(string oldValue, string newValue)
    {
        if (_currentIndex is not null && !IsBusy)
        {
            _ = ApplyFilterAsync();
        }
    }

    public bool IsFlatViewMode => !string.IsNullOrWhiteSpace(ActiveSearchText)
        || (!string.IsNullOrEmpty(SelectedType) && SelectedType != _allTypesLabel);

    public bool IsDirectoryViewMode => !IsFlatViewMode;

    [ObservableProperty]
    public partial AssetSortField SelectedSortField { get; set; } = AssetSortField.IndexOrder;

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial GridLength ColumnWidthName { get; set; } = new(240, GridUnitType.Pixel);

    [ObservableProperty]
    public partial GridLength ColumnWidthContainer { get; set; } = new(200, GridUnitType.Pixel);

    [ObservableProperty]
    public partial GridLength ColumnWidthType { get; set; } = new(110, GridUnitType.Pixel);

    [ObservableProperty]
    public partial GridLength ColumnWidthPathId { get; set; } = new(100, GridUnitType.Pixel);

    [ObservableProperty]
    public partial GridLength ColumnWidthSize { get; set; } = new(100, GridUnitType.Pixel);

    public string DecompressionSummary => Settings.DecompressionMode switch
    {
        BundleDecompressionMode.Memory => T("DecompressionMemory"),
        BundleDecompressionMode.Disk => T("DecompressionDisk", Settings.DecompressionDirectory),
        _ => T("DecompressionAuto"),
    };

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    [NotifyPropertyChangedFor(nameof(ShowPreviewImage))]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMesh))]
    [NotifyPropertyChangedFor(nameof(ShowPreviewImage))]
    public partial MeshGeometryData? PreviewMeshGeometry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeshWireframeButtonText))]
    public partial int MeshWireframeMode { get; set; } = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeshShadeButtonText))]
    public partial int MeshShadeMode { get; set; } = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeshNormalsButtonText))]
    public partial bool MeshUseCalculatedNormals { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewText))]
    public partial string? PreviewText { get; set; }

    [ObservableProperty]
    public partial string PreviewMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AssetInformation { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAsset))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanExportAnimator))]
    [NotifyPropertyChangedFor(nameof(CanPlayAudio))]
    public partial AssetRowViewModel? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string DumpText { get; set; } = string.Empty;

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

    public bool HasPreviewMesh => PreviewMeshGeometry is not null;

    public bool ShowPreviewImage => HasPreviewImage && !HasPreviewMesh;

    public bool HasPreviewText => !string.IsNullOrEmpty(PreviewText);

    public string MeshWireframeButtonText => MeshWireframeMode switch
    {
        1 => T("MeshWireframe"),
        2 => T("MeshShadedWireframe"),
        _ => T("MeshShaded")
    };

    public string MeshShadeButtonText => MeshShadeMode switch
    {
        1 => T("MeshVertexColor"),
        _ => T("MeshLighting")
    };

    public string MeshNormalsButtonText => MeshUseCalculatedNormals
        ? T("MeshSmoothNormals")
        : T("MeshOriginalNormals");

    public void CycleMeshWireframeMode()
    {
        MeshWireframeMode = (MeshWireframeMode + 1) % 3;
    }

    public void CycleMeshShadeMode()
    {
        MeshShadeMode = (MeshShadeMode + 1) % 2;
    }

    public void ToggleMeshNormals()
    {
        MeshUseCalculatedNormals = !MeshUseCalculatedNormals;
    }

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
        StatusText = forceRebuild ? T("RebuildingIndex") : T("CheckingIndex");
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
            AssetTypes.Add(_allTypesLabel);
            foreach (var typeName in typeCounts.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                AssetTypes.Add(typeName);
                AssetClasses.Add(new AssetClassRowViewModel(typeName, typeCounts[typeName]));
            }
            SceneRoots.Clear();
            _sceneHierarchyRoots = null;
            _containerHierarchyRoots = null;
            ContainerTreeRoots.Clear();
            SelectedContainerPath = null;
            SelectedContainerTitle = null;
            SelectedContainerExact = false;
            SelectedContainerNode = null;
            SelectedType = _allTypesLabel;
            ActiveSearchText = string.Empty;
            _totalAssetCount = result.AssetCount;
            _pageOffset = 0;
            await LoadPageAsync(0, cancellationToken);
            _ = _currentIndex.WarmupAsync(cancellationToken);
            _ = BuildContainerTreeAsync(cancellationToken);
            await RememberSourceAsync(_sourcePaths, fileSelection, cancellationToken);
            StatusText = result.ReusedExistingIndex
                ? T("OpenedCachedIndex", result.AssetCount)
                : T("BuiltStreamingIndex", result.AssetCount, result.Decompression.Mode);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("LoadingCancelled");
        }
        catch (Exception exception)
        {
            StatusText = T("OpenFailed", exception.Message);
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

    public event Action? RecentSourcesChanged;

    public void CancelLoad() => _loadCancellation?.Cancel();

    public async Task OpenRecentSourceAsync(RecentSource recent)
    {
        if (recent.Paths.Count == 0)
        {
            return;
        }

        var exists = recent.Paths.All(path => recent.IsFileSelection ? File.Exists(path) : Directory.Exists(path));
        if (!exists)
        {
            StatusText = T("RecentSourceUnavailable");
            return;
        }

        await OpenSourcesAsync(recent.Paths, recent.IsFileSelection, false);
    }

    public async Task ClearRecentSourcesAsync()
    {
        Settings.RecentSources.Clear();
        if (_settingsStore is not null)
        {
            await _settingsStore.SaveAsync(Settings);
        }
        OnPropertyChanged(nameof(CanOpenRecent));
        RecentSourcesChanged?.Invoke();
    }

    public async Task OpenMostRecentAsync()
    {
        var recent = Settings.RecentSources.FirstOrDefault();
        if (recent is not null)
        {
            await OpenRecentSourceAsync(recent);
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
            StatusText = T("RecentSourceUnavailable");
        }
    }

    public Task ApplyFilterAsync()
    {
        ActiveSearchText = SearchText.Trim();
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
        DumpText = row is null ? T("SelectAssetLoadDump") : T("ChooseLoadDump");
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var cancellationToken = _previewCancellation.Token;
        IsPreviewBusy = false;

        var previousImage = PreviewImage;
        PreviewImage = null;
        PreviewMeshGeometry = null;
        PreviewText = null;
        previousImage?.Dispose();
        if (row is null)
        {
            PreviewMessage = T("SelectTexturePreview");
            AssetInformation = T("NoAssetSelected");
            return;
        }

        AssetInformation = $"{row.Name}\n{row.Type}\nPathID: {row.PathId}\nStored size: {row.Size:N0} bytes";
        if (_previewService is null)
        {
            PreviewMessage = T("IndexNotOpen");
            return;
        }
        if (!_previewService.Supports(row.Type))
        {
            PreviewMessage = T("PreviewNotImplemented", row.Type);
            return;
        }

        IsPreviewBusy = true;
        PreviewMessage = T("LoadingPreview", row.Type);
        try
        {
            var preview = await _previewService.LoadAsync(row.IndexEntry, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (preview.PngData is not null)
            {
                using var stream = new MemoryStream(preview.PngData, writable: false);
                PreviewImage = new Bitmap(stream);
            }
            PreviewMeshGeometry = preview.MeshGeometry;
            PreviewText = preview.Text;
            PreviewMessage = preview.FromCache ? T("PreviewCacheHit") : string.Empty;
            AssetInformation = preview.Information;
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this preview.
        }
        catch (Exception exception)
        {
            PreviewMessage = T("PreviewFailed", exception.Message);
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

    public async Task<IReadOnlyList<SceneHierarchyNode>> GetOrLoadSceneHierarchyAsync(CancellationToken cancellationToken = default)
    {
        if (_sceneHierarchyRoots is not null)
        {
            return _sceneHierarchyRoots;
        }

        if (_currentIndex is null)
        {
            return [];
        }

        var roots = await new SceneHierarchyService().BuildAsync(_currentIndex, cancellationToken);
        _sceneHierarchyRoots = roots;
        return roots;
    }

    public async Task LoadHierarchyAsync()
    {
        if (_currentIndex is null || IsHierarchyBusy)
        {
            return;
        }

        IsHierarchyBusy = true;
        StatusText = T("BuildingHierarchy");
        try
        {
            var roots = await GetOrLoadSceneHierarchyAsync();
            var localizer = _localizer;
            var viewModels = await Task.Run(() =>
            {
                var list = new List<SceneNodeViewModel>(roots.Count);
                for (int i = 0; i < roots.Count; i++)
                {
                    list.Add(SceneNodeViewModel.FromNode(roots[i], localizer));
                }
                return list;
            });

            SceneRoots.ReplaceAll(viewModels);
            StatusText = T("HierarchyLoaded", roots.Count);
        }
        catch (Exception exception)
        {
            StatusText = T("HierarchyFailed", exception.Message);
        }
        finally
        {
            IsHierarchyBusy = false;
        }
    }

    public async Task BuildContainerTreeAsync(CancellationToken cancellationToken = default)
    {
        if (_currentIndex is null || IsContainerTreeBusy)
        {
            return;
        }

        IsContainerTreeBusy = true;
        try
        {
            var service = new ContainerHierarchyService();
            _containerHierarchyRoots = await Task.Run(async () =>
            {
                return await service.BuildAsync(_currentIndex, cancellationToken);
            }, cancellationToken);

            var localizer = _localizer;
            var viewModels = await Task.Run(() =>
            {
                var list = new List<ContainerTreeNodeViewModel>(_containerHierarchyRoots.Count);
                for (int i = 0; i < _containerHierarchyRoots.Count; i++)
                {
                    list.Add(ContainerTreeNodeViewModel.FromNode(_containerHierarchyRoots[i], localizer));
                }
                return list;
            }, cancellationToken);

            ContainerTreeRoots.ReplaceAll(viewModels);
            OnPropertyChanged(nameof(ContainerTreeSummary));
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception exception)
        {
            StatusText = T("HierarchyFailed", exception.Message);
        }
        finally
        {
            IsContainerTreeBusy = false;
        }
    }

    public async Task SelectContainerNodeAsync(ContainerTreeNodeViewModel? node)
    {
        SelectedContainerNode = node;
        string? newPath;
        string? newTitle;
        bool newExact;

        if (node is null || string.IsNullOrEmpty(node.FullPath))
        {
            newPath = null;
            newTitle = null;
            newExact = false;
        }
        else if (node.FullPath == "(No Container)")
        {
            newPath = "(No Container)";
            newTitle = _localizer["NoContainerGroup"];
            newExact = true;
        }
        else
        {
            newPath = node.FullPath;
            newTitle = node.Name;
            newExact = !node.IsDirectory || node.Children.Count == 0;
        }

        if (SelectedContainerPath == newPath && SelectedContainerExact == newExact)
        {
            return;
        }

        SelectedContainerPath = newPath;
        SelectedContainerTitle = newTitle;
        SelectedContainerExact = newExact;
        await ApplyFilterAsync();
    }

    public async Task ClearContainerFilterAsync()
    {
        if (SelectedContainerPath is null && !SelectedContainerExact && SelectedContainerNode is null)
        {
            return;
        }
        SelectedContainerPath = null;
        SelectedContainerTitle = null;
        SelectedContainerExact = false;
        SelectedContainerNode = null;
        await ApplyFilterAsync();
    }

    public Task SelectAssetClassAsync(AssetClassRowViewModel? assetClass)
    {
        if (assetClass is null || SelectedType == assetClass.TypeName)
        {
            return Task.CompletedTask;
        }
        SelectedType = assetClass.TypeName;
        return Task.CompletedTask;
    }

    public void SelectSceneNode(SceneNodeViewModel? node) => SelectedSceneNode = node;

    public async Task ExportSelectedSceneModelAsync(string outputDirectory)
    {
        if (SelectedSceneNode is null || _gameObjectExportService is null || IsExportBusy)
        {
            return;
        }
        IsExportBusy = true;
        StatusText = T("ExportingSceneModel", SelectedSceneNode.Name);
        try
        {
            var result = await _gameObjectExportService.ExportAsync(SelectedSceneNode.TransformEntry, outputDirectory);
            StatusText = T("SceneModelExported", result.Files.Count);
        }
        catch (Exception exception)
        {
            StatusText = T("SceneModelExportFailed", exception.Message);
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
        DumpText = T("LoadingDump");
        _dumpCancellation?.Dispose();
        _dumpCancellation = new CancellationTokenSource();
        var cancellationToken = _dumpCancellation.Token;
        try
        {
            var result = await _inspectionService.LoadDumpAsync(SelectedAsset.IndexEntry, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DumpText = result.Text;
            StatusText = result.IsTruncated ? T("DumpTruncated") : T("DumpLoaded");
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this dump.
        }
        catch (Exception exception)
        {
            DumpText = T("DumpFailed", exception.Message);
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
        StatusText = T("ExportingAnimator");
        try
        {
            var result = await _animatorExportService.ExportAsync(SelectedAsset.IndexEntry, outputDirectory);
            StatusText = T("AnimatorExported", result.Files.Count);
        }
        catch (Exception exception)
        {
            StatusText = T("AnimatorExportFailed", exception.Message);
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
        StatusText = T("Converting", SelectedAsset.Type);
        try
        {
            var result = await _convertedExportService.ExportAsync(SelectedAsset.IndexEntry, outputDirectory);
            StatusText = result.Note ?? T("ConvertedExported", result.Files.Count);
        }
        catch (Exception exception)
        {
            StatusText = T("ConvertedExportFailed", exception.Message);
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
        StatusText = T("PreparingAudio");
        try
        {
            var settings = Settings.Normalize(_directories);
            var playbackDirectory = Path.Combine(new CacheLayout(settings).Previews, "audio-playback");
            var result = await _convertedExportService.ExportAsync(SelectedAsset.IndexEntry, playbackDirectory);
            var file = result.Files.FirstOrDefault() ?? throw new InvalidDataException(T("AudioExportNoFile"));
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
            StatusText = result.Note ?? T("OpenedAudio");
        }
        catch (Exception exception)
        {
            StatusText = T("AudioPlaybackFailed", exception.Message);
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
                string.IsNullOrWhiteSpace(ActiveSearchText) ? null : ActiveSearchText,
                SelectedType == _allTypesLabel ? null : SelectedType,
                SelectedContainerPath,
                SelectedContainerExact,
                cancellationToken);
        IsExportBusy = true;
        IsBatchExportBusy = true;
        StatusText = selectedOnly ? T("ExportingSelected") : T("ExportingFiltered");
        var progress = new Progress<BatchExportProgress>(value =>
        {
            StatusText = T("ExportProgress", value.Completed, value.Succeeded, value.Failed, value.AssetName);
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
                ? T("BatchExportComplete", result.Succeeded)
                : T("BatchExportCompleteWithFailures", result.Succeeded, result.Failed, result.ErrorLogPath);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("BatchExportCancelled");
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
        StatusText = T("ExtractingBundles");
        try
        {
            var progress = new Progress<BundleExtractionProgress>(value =>
            {
                StatusText = T("ExtractionProgress", value.ExtractedFiles, value.CompletedSources, value.TotalSources, Path.GetFileName(value.SourcePath));
            });
            var count = await _bundleExtractionService.ExtractAsync(_sourcePaths, outputDirectory, progress, token);
            StatusText = T("ExtractionComplete", count);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("ExtractionCancelled");
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
        StatusText = T("WritingAssetList");
        try
        {
            var count = await _assetListExportService.ExportAsync(
                _currentIndex,
                outputPath,
                format,
                string.IsNullOrWhiteSpace(ActiveSearchText) ? null : ActiveSearchText,
                SelectedType == _allTypesLabel ? null : SelectedType,
                _exportCancellation.Token);
            StatusText = T("AssetListExported", count);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("AssetListCancelled");
        }
        catch (Exception exception)
        {
            StatusText = T("AssetListExportFailed", exception.Message);
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
        StatusText = dump ? T("ExportingDump") : T("StreamingRaw");
        try
        {
            var result = dump
                ? await _exportService.ExportDumpAsync(SelectedAsset.IndexEntry, outputPath)
                : await _exportService.ExportRawAsync(SelectedAsset.IndexEntry, outputPath);
            StatusText = T("ExportedFiles", result.Files.Count, result.Files.Count == 1 ? string.Empty : "s");
        }
        catch (Exception exception)
        {
            StatusText = T("ExportFailed", exception.Message);
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
        var query = new AssetIndexQuery(
            offset,
            PageSize,
            string.IsNullOrWhiteSpace(ActiveSearchText) ? null : ActiveSearchText,
            SelectedType == _allTypesLabel ? null : SelectedType,
            SelectedContainerPath,
            SelectedContainerExact,
            SelectedSortField,
            SortDescending);
        AssetIndexPage page;
        try
        {
            page = await Task.Run(() => _currentIndex.QueryAsync(query, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        if (generation != _pageGeneration)
        {
            return;
        }
        var rows = new List<AssetRowViewModel>(page.Items.Count);
        foreach (var entry in page.Items)
        {
            rows.Add(new AssetRowViewModel(
                entry.Name,
                entry.Container ?? string.Empty,
                entry.TypeName,
                entry.PathId,
                entry.ByteSize,
                entry));
        }
        Assets.ReplaceAll(rows);
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
        if (Settings.RecentSources.Count > 20)
        {
            Settings.RecentSources.RemoveRange(20, Settings.RecentSources.Count - 20);
        }
        await _settingsStore.SaveAsync(Settings, cancellationToken);
        OnPropertyChanged(nameof(CanOpenRecent));
        RecentSourcesChanged?.Invoke();
    }

    private string T(string key, params object?[] arguments) => arguments.Length == 0
        ? _localizer[key]
        : _localizer.Format(key, arguments);

    private void LocalizerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != "Item[]")
        {
            return;
        }

        var selectedAllTypes = SelectedType == _allTypesLabel;
        var index = AssetTypes.IndexOf(_allTypesLabel);
        _allTypesLabel = _localizer["AllTypes"];
        if (index >= 0)
        {
            AssetTypes[index] = _allTypesLabel;
        }
        if (selectedAllTypes)
        {
            SelectedType = _allTypesLabel;
        }
        if (_containerHierarchyRoots is not null)
        {
            var viewModels = new List<ContainerTreeNodeViewModel>(_containerHierarchyRoots.Count);
            foreach (var root in _containerHierarchyRoots)
            {
                viewModels.Add(ContainerTreeNodeViewModel.FromNode(root, _localizer));
            }
            ContainerTreeRoots.ReplaceAll(viewModels);
        }
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(DecompressionSummary));
        OnPropertyChanged(nameof(ContainerTreeSummary));
        OnPropertyChanged(nameof(MeshWireframeButtonText));
        OnPropertyChanged(nameof(MeshShadeButtonText));
        OnPropertyChanged(nameof(MeshNormalsButtonText));
        OnPropertyChanged(nameof(IsFlatViewMode));
        OnPropertyChanged(nameof(IsDirectoryViewMode));
    }

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    public void Dispose()
    {
        _localizer.PropertyChanged -= LocalizerChanged;
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
        _httpServer?.Dispose();
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
    AssetIndexEntry IndexEntry)
{
    public string SizeMegabytes => (Size / 1024d / 1024d).ToString("N2");
}

public sealed record AssetClassRowViewModel(string TypeName, long Count);

public sealed class SceneNodeViewModel
{
    private readonly SceneHierarchyNode _node;
    private readonly AppLocalizer _localizer;
    private IReadOnlyList<SceneNodeViewModel>? _children;
    private string? _details;

    public SceneNodeViewModel(SceneHierarchyNode node, AppLocalizer localizer)
    {
        _node = node;
        _localizer = localizer;
        Name = node.Name;
        TransformEntry = node.TransformEntry;
    }

    public string Name { get; }
    public string Details => _details ??= _localizer.Format("ScenePathId", _node.GameObjectPathId);
    public AssetIndexEntry TransformEntry { get; }

    public IReadOnlyList<SceneNodeViewModel> Children
    {
        get
        {
            if (_children is not null) return _children;
            if (_node.Children.Count == 0)
            {
                return _children = [];
            }

            var array = new SceneNodeViewModel[_node.Children.Count];
            for (int i = 0; i < _node.Children.Count; i++)
            {
                array[i] = new SceneNodeViewModel(_node.Children[i], _localizer);
            }
            return _children = array;
        }
    }

    public static SceneNodeViewModel FromNode(SceneHierarchyNode node, AppLocalizer localizer) =>
        new(node, localizer);
}


public sealed class ContainerTreeNodeViewModel
{
    private readonly ContainerHierarchyNode _node;
    private readonly AppLocalizer _localizer;
    private IReadOnlyList<ContainerTreeNodeViewModel>? _children;

    public ContainerTreeNodeViewModel(ContainerHierarchyNode node, AppLocalizer localizer)
    {
        _node = node;
        _localizer = localizer;
        Name = node.Name switch
        {
            "AllAssets" => localizer["AllAssets"],
            "(No Container)" => localizer["NoContainerGroup"],
            _ => node.Name
        };
        FullPath = node.FullPath;
        Details = localizer.Format("ContainerItemCount", node.TotalAssetCount);
        IsDirectory = node.IsDirectory;
        TotalAssetCount = node.TotalAssetCount;
        Icon = node.Name switch
        {
            "AllAssets" => "🌐",
            "(No Container)" => "📁",
            _ => node.IsDirectory && node.Children.Count > 0 ? "📁" : "📦"
        };
    }

    public string Name { get; }
    public string FullPath { get; }
    public string Details { get; }
    public string Icon { get; }
    public bool IsDirectory { get; }
    public int TotalAssetCount { get; }
    public FontWeight NameFontWeight => IsDirectory ? FontWeight.SemiBold : FontWeight.Normal;

    public IReadOnlyList<ContainerTreeNodeViewModel> Children
    {
        get
        {
            if (_children is not null) return _children;
            if (_node.Children.Count == 0)
            {
                return _children = [];
            }

            var array = new ContainerTreeNodeViewModel[_node.Children.Count];
            for (int i = 0; i < _node.Children.Count; i++)
            {
                array[i] = new ContainerTreeNodeViewModel(_node.Children[i], _localizer);
            }
            return _children = array;
        }
    }

    public static ContainerTreeNodeViewModel FromNode(ContainerHierarchyNode node, AppLocalizer localizer) =>
        new(node, localizer);
}
