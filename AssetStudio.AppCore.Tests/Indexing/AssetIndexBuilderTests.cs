using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class AssetIndexBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"assetstudio-builder-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpenAsyncHandlesEmptyDirectoryGracefully()
    {
        var settings = new AppSettings
        {
            CacheRoot = Path.Combine(_root, "cache"),
        };
        var cacheLayout = new CacheLayout(settings);
        var builder = new AssetIndexBuilder(settings, cacheLayout);

        var emptyDir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = await builder.OpenAsync(emptyDir);

        Assert.NotNull(result);
        Assert.Equal(0, result.AssetCount);
        Assert.False(result.ReusedExistingIndex);
        Assert.Equal("Empty", result.Decompression.Mode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
