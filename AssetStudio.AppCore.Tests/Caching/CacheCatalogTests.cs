using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Caching;

public sealed class CacheCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"assetstudio-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListsAndDeletesOnlyItsOwnIndexDirectory()
    {
        var settings = new AppSettings { CacheRoot = _root }.Normalize(new AppDirectories(_root, _root));
        var layout = new CacheLayout(settings);
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        var index = new DiskAssetIndex(layout.Indexes, source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries());

        var catalog = new CacheCatalog(layout);
        var entries = await catalog.ListAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(source, entry.SourcePath);
        Assert.Equal(1, entry.AssetCount);
        Assert.True(entry.CacheBytes > 0);
        catalog.Delete(entry);
        Assert.Empty(await catalog.ListAsync());
    }

    private static async IAsyncEnumerable<AssetIndexEntry> Entries()
    {
        yield return new AssetIndexEntry(0, "source", "file", "file", 1, 28, "Texture2D", "texture", null, 0, 10);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
