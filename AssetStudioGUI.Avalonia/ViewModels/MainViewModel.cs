using System.Collections.ObjectModel;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
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
    private CancellationTokenSource? _previewCancellation;
    private string? _sourcePath;
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

    public AppSettings Settings { get; }

    public bool HasSource => _sourcePath is not null;

    public bool CanGoPrevious => !IsBusy && _pageOffset > 0;

    public bool CanGoNext => !IsBusy && NextOffset is not null;

    public int? NextOffset { get; private set; }

    public string PageSummary => _currentIndex is null
        ? "No index open"
        : $"Rows {_pageOffset + 1}–{_pageOffset + Assets.Count}";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

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

    public async Task OpenSourceAsync(string sourcePath, bool forceRebuild = false)
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
            var result = await Task.Run(() => builder.OpenAsync(sourcePath, forceRebuild, progress));
            _sourcePath = result.Fingerprint.RootPath;
            _currentIndex = result.Index;
            _previewService = new TexturePreviewService(
                new AssetObjectLoader(settings, layout),
                new MemoryPreviewCache(settings.PreviewCacheMegabytes));
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

    public Task RebuildAsync() => _sourcePath is null
        ? Task.CompletedTask
        : OpenSourceAsync(_sourcePath, true);

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

    private async Task LoadPageAsync(int offset)
    {
        if (_currentIndex is null)
        {
            return;
        }

        var page = await _currentIndex.QueryAsync(new AssetIndexQuery(offset, PageSize, SearchText));
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
