using System.Runtime.CompilerServices;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class DiskAssetIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetStudioCat.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildAndDirectPagingDoNotRequireLoadingAllRows()
    {
        var source = CreateSourceDirectory();
        var fingerprint = AssetSourceFingerprint.Create(source);
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);

        await index.BuildAsync(fingerprint, Entries(600));
        var page = await index.QueryAsync(new AssetIndexQuery(250, 100));

        Assert.True(await index.IsCurrentAsync(fingerprint));
        Assert.Equal(600, page.TotalCount);
        Assert.Equal(350, page.NextOffset);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(250, page.Items[0].Id);
        Assert.Equal(349, page.Items[^1].Id);
    }

    [Fact]
    public async Task FilterMatchesNameContainerAndType()
    {
        var source = CreateSourceDirectory();
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries(30));

        var page = await index.QueryAsync(new AssetIndexQuery(
            Limit: 10,
            SearchText: "group-2",
            TypeName: "Texture2D"));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item =>
        {
            Assert.Equal("Texture2D", item.TypeName);
            Assert.Contains("group-2", item.Container);
        });
    }

    [Fact]
    public async Task ChangedSourceInvalidatesAndRebuildRemovesIndex()
    {
        var source = CreateSourceDirectory();
        var original = AssetSourceFingerprint.Create(source);
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(original, Entries(2));

        await File.AppendAllTextAsync(Path.Combine(source, "bundle-a"), "changed");
        var changed = AssetSourceFingerprint.Create(source);

        Assert.False(await index.IsCurrentAsync(changed));
        index.Rebuild();
        Assert.False(Directory.Exists(index.DirectoryPath));
    }

    private string CreateSourceDirectory()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "bundle-a"), "bundle-content");
        return source;
    }

    private static async IAsyncEnumerable<AssetIndexEntry> Entries(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AssetIndexEntry(
                i,
                "/source",
                "/source/bundle-a",
                "data.assets",
                i + 1,
                i % 2 == 0 ? 28 : 49,
                i % 2 == 0 ? "Texture2D" : "TextAsset",
                $"asset-{i}",
                $"group-{i % 5}",
                i * 128,
                128);
            await Task.Yield();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
