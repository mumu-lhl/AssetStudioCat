using AssetStudio.AppCore.Configuration;

namespace AssetStudio.AppCore.Tests.Configuration;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"assetstudio-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadRoundTripsSettings()
    {
        var directories = new AppDirectories(
            Path.Combine(_root, "config"),
            Path.Combine(_root, "cache"));
        var store = new AppSettingsStore(directories);
        var settings = new AppSettings
        {
            DecompressionMode = BundleDecompressionMode.Disk,
            DecompressionDirectory = Path.Combine(_root, "hdd-cache"),
            PreviewCacheMegabytes = 192,
            ConvertedImageFormat = ConvertedImageFormat.Webp,
            FbxScaleFactor = 0.01,
            RestoreLastSource = false,
            RecentSources = [new RecentSource(["/games/one"], false, DateTimeOffset.UtcNow)],
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(BundleDecompressionMode.Disk, loaded.DecompressionMode);
        Assert.Equal(Path.GetFullPath(settings.DecompressionDirectory), loaded.DecompressionDirectory);
        Assert.Equal(192, loaded.PreviewCacheMegabytes);
        Assert.Equal(ConvertedImageFormat.Webp, loaded.ConvertedImageFormat);
        Assert.Equal(0.01, loaded.FbxScaleFactor);
        Assert.False(loaded.RestoreLastSource);
        Assert.Single(loaded.RecentSources);
    }

    [Fact]
    public async Task InvalidJsonFallsBackToDefaults()
    {
        var directories = new AppDirectories(
            Path.Combine(_root, "config"),
            Path.Combine(_root, "cache"));
        Directory.CreateDirectory(directories.ConfigDirectory);
        await File.WriteAllTextAsync(Path.Combine(directories.ConfigDirectory, "settings.json"), "not-json");

        var loaded = await new AppSettingsStore(directories).LoadAsync();

        Assert.Equal(BundleDecompressionMode.Auto, loaded.DecompressionMode);
        Assert.Equal(Path.GetFullPath(directories.DefaultCacheDirectory), loaded.CacheRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
