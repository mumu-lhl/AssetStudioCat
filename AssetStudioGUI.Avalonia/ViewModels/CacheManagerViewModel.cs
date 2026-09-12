using System.Collections.ObjectModel;
using AssetStudio.AppCore.Caching;

namespace AssetStudioGUI.Avalonia.ViewModels;

public sealed class CacheManagerViewModel(CacheCatalog catalog) : ViewModelBase
{
    public ObservableCollection<CacheIndexRowViewModel> Entries { get; } = [];

    public string Summary => $"{Entries.Count:N0} indexes — {Entries.Sum(entry => entry.Info.CacheBytes) / 1024d / 1024d:N1} MB";

    public async Task RefreshAsync()
    {
        var entries = await catalog.ListAsync();
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new CacheIndexRowViewModel(entry));
        }
        OnPropertyChanged(nameof(Summary));
    }

    public async Task DeleteAsync(CacheIndexRowViewModel? entry)
    {
        if (entry is null) return;
        catalog.Delete(entry.Info);
        await RefreshAsync();
    }

    public async Task ClearAsync()
    {
        catalog.Clear();
        await RefreshAsync();
    }
}

public sealed record CacheIndexRowViewModel(CacheIndexInfo Info)
{
    public string Source => Info.SourceFileCount > 1 ? $"{Info.SourcePath} ({Info.SourceFileCount:N0} files)" : Info.SourcePath;
    public string Assets => Info.AssetCount.ToString("N0");
    public string Size => $"{Info.CacheBytes / 1024d / 1024d:N1} MB";
    public string Created => Info.CreatedAt.LocalDateTime.ToString("g");
}
