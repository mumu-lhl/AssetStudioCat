using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.CustomOptions;

namespace AssetStudio.AppCore.Loading;

public sealed class DecompressionSession : IDisposable
{
    private const string MarkerFileName = ".assetstudio-session";
    private bool _disposed;

    private DecompressionSession(string directory, DecompressionDecision decision)
    {
        Directory = directory;
        Decision = decision;
    }

    public string Directory { get; }

    public DecompressionDecision Decision { get; }

    public static DecompressionSession Create(
        AppSettings settings,
        CacheLayout cacheLayout,
        long estimatedUncompressedBytes,
        long? availableMemoryBytes = null)
    {
        var available = availableMemoryBytes ?? GetAvailableMemoryBytes();
        var decision = DecompressionPolicy.Decide(settings, estimatedUncompressedBytes, available);
        var directory = cacheLayout.CreateSessionDirectory();
        File.WriteAllText(Path.Combine(directory, MarkerFileName), DateTime.UtcNow.ToString("O"));
        return new DecompressionSession(directory, decision);
    }

    public void ApplyTo(CustomBundleOptions options)
    {
        options.DecompressToDisk = Decision.EffectiveMode == BundleDecompressionMode.Disk;
        options.DecompressionDirectory = options.DecompressToDisk ? Directory : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        var markerPath = Path.Combine(Directory, MarkerFileName);
        if (File.Exists(markerPath))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private static long GetAvailableMemoryBytes()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return available > 0 ? available : 2L * 1024 * 1024 * 1024;
    }
}
