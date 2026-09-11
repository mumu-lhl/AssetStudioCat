namespace AssetStudio.AppCore.Indexing;

public sealed record AssetIndexQuery(
    int Offset = 0,
    int Limit = 250,
    string? SearchText = null,
    string? TypeName = null)
{
    public AssetIndexQuery Normalize() => this with
    {
        Offset = Math.Max(0, Offset),
        Limit = Math.Clamp(Limit, 1, 2_000),
        SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        TypeName = string.IsNullOrWhiteSpace(TypeName) ? null : TypeName.Trim(),
    };
}
