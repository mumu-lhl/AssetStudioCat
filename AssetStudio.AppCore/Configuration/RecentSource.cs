namespace AssetStudio.AppCore.Configuration;

public sealed record RecentSource(
    IReadOnlyList<string> Paths,
    bool IsFileSelection,
    DateTimeOffset LastOpenedAt)
{
    public string DisplayName => IsFileSelection
        ? $"{Paths.Count} selected files — {Path.GetDirectoryName(Paths.FirstOrDefault())}"
        : Paths.FirstOrDefault() ?? "Unknown source";
}
