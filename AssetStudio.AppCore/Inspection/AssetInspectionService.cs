using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;

namespace AssetStudio.AppCore.Inspection;

public sealed class AssetInspectionService
{
    private readonly AssetObjectLoader _objectLoader;

    public AssetInspectionService(AssetObjectLoader objectLoader)
    {
        _objectLoader = objectLoader;
    }

    public async Task<AssetDumpResult> LoadDumpAsync(
        AssetIndexEntry entry,
        int maximumCharacters = 2_000_000,
        CancellationToken cancellationToken = default)
    {
        maximumCharacters = Math.Clamp(maximumCharacters, 1_000, 16_000_000);
        using var session = await _objectLoader.OpenAsync(entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var text = session.Asset.Dump();
        if (string.IsNullOrEmpty(text))
        {
            text = session.Asset.DumpObject();
        }
        if (string.IsNullOrEmpty(text))
        {
            throw new NotSupportedException($"A dump could not be produced for {entry.TypeName}.");
        }

        if (text.Length <= maximumCharacters)
        {
            return new AssetDumpResult(text, false);
        }

        return new AssetDumpResult(
            text[..maximumCharacters] + "\n\n[Dump truncated in the preview. Export the dump to inspect the complete object.]",
            true);
    }
}
