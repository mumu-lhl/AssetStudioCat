namespace AssetStudio.AppCore.Configuration;

public sealed record RecentSource(
    IReadOnlyList<string> Paths,
    bool IsFileSelection,
    DateTimeOffset LastOpenedAt)
{
    public string DisplayName
    {
        get
        {
            if (!IsFileSelection)
            {
                return Paths.FirstOrDefault() ?? "Unknown source";
            }
            if (Paths.Count == 1)
            {
                return Paths[0];
            }
            if (Paths.Count > 1)
            {
                var dir = Path.GetDirectoryName(Paths[0]);
                return $"{Path.GetFileName(Paths[0])} (+{Paths.Count - 1}) — {dir}";
            }
            return "Unknown source";
        }
    }
}
