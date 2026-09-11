using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;

namespace AssetStudio.AppCore.Preview;

public sealed class TexturePreviewService
{
    private readonly AssetObjectLoader _objectLoader;
    private readonly MemoryPreviewCache _cache;

    public TexturePreviewService(AssetObjectLoader objectLoader, MemoryPreviewCache cache)
    {
        _objectLoader = objectLoader;
        _cache = cache;
    }

    public async Task<TexturePreview> LoadAsync(
        AssetIndexEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!entry.TypeName.Equals(nameof(Texture2D), StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Preview for {entry.TypeName} is not implemented yet.");
        }

        var cacheKey = $"{entry.ObjectSourcePath}\n{entry.SerializedFile}\n{entry.PathId}";
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return new TexturePreview(cached, Describe(entry, null), true);
        }

        using var session = await _objectLoader.OpenAsync(entry, cancellationToken);
        if (session.Asset is not Texture2D texture)
        {
            throw new InvalidDataException($"PathID {entry.PathId} is not a Texture2D.");
        }

        using var stream = texture.ConvertToStream(ImageFormat.Png, flip: true)
            ?? throw new NotSupportedException($"Texture format {texture.m_TextureFormat} could not be decoded.");
        var png = stream.ToArray();
        _cache.Set(cacheKey, png);
        return new TexturePreview(png, Describe(entry, texture), false);
    }

    private static string Describe(AssetIndexEntry entry, Texture2D? texture)
    {
        if (texture is null)
        {
            return $"{entry.Name}\nTexture2D\nPathID: {entry.PathId}\nDecoded preview cache";
        }

        return $"{entry.Name}\nTexture2D\n{texture.m_Width} × {texture.m_Height}\n" +
               $"Format: {texture.m_TextureFormat}\nPathID: {entry.PathId}\nStored size: {entry.ByteSize:N0} bytes";
    }
}
