using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Loading;

namespace AssetStudio.AppCore.Tests.Caching;

public sealed class CacheLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"assetstudio-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreatesAndVerifiesCacheDirectories()
    {
        var settings = CreateSettings();
        var layout = new CacheLayout(settings);

        await layout.VerifyWritableAsync();

        Assert.True(Directory.Exists(layout.Indexes));
        Assert.True(Directory.Exists(layout.Previews));
        Assert.True(Directory.Exists(layout.Sessions));
    }

    [Fact]
    public void SessionAppliesDiskDirectoryAndCleansItOnDispose()
    {
        var settings = CreateSettings();
        settings.DecompressionMode = BundleDecompressionMode.Disk;
        var layout = new CacheLayout(settings);
        string directory;

        using (var session = DecompressionSession.Create(settings, layout, 4L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024))
        {
            directory = session.Directory;
            var options = new AssetStudio.CustomOptions.CustomBundleOptions();
            session.ApplyTo(options);

            Assert.True(options.DecompressToDisk);
            Assert.Equal(directory, options.DecompressionDirectory);
            Assert.True(Directory.Exists(directory));
        }

        Assert.False(Directory.Exists(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private AppSettings CreateSettings()
    {
        var directories = new AppDirectories(Path.Combine(_root, "config"), Path.Combine(_root, "cache"));
        return new AppSettings().Normalize(directories);
    }
}
