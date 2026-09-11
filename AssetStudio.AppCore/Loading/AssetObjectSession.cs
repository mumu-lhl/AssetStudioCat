using global::AssetStudio;

namespace AssetStudio.AppCore.Loading;

public sealed class AssetObjectSession : IDisposable
{
    private readonly AssetsManager _manager;
    private readonly DecompressionSession _decompressionSession;
    private bool _disposed;

    internal AssetObjectSession(
        AssetsManager manager,
        DecompressionSession decompressionSession,
        global::AssetStudio.Object asset)
    {
        _manager = manager;
        _decompressionSession = decompressionSession;
        Asset = asset;
    }

    public global::AssetStudio.Object Asset { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.Clear();
        _decompressionSession.Dispose();
    }
}
