using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class SceneHierarchyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetStudioCat.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildsNestedGameObjectsFromTransformReferences()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "bundle"), "data");
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries());

        var roots = await new SceneHierarchyService().BuildAsync(index);

        var root = Assert.Single(roots);
        Assert.Equal("Root", root.Name);
        Assert.Equal("Child", Assert.Single(root.Children).Name);
    }

    private static async IAsyncEnumerable<AssetIndexEntry> Entries()
    {
        const string file = "/virtual/scene.assets";
        yield return Entry(1, "GameObject", "Root", file);
        yield return Entry(2, "GameObject", "Child", file);
        yield return Entry(10, "Transform", "Transform #10", file, 1, file);
        await Task.Yield();
        yield return Entry(11, "Transform", "Transform #11", file, 2, file, 10, file);
    }

    private static AssetIndexEntry Entry(
        long id,
        string type,
        string name,
        string file,
        long? gameObject = null,
        string? gameObjectFile = null,
        long? parent = null,
        string? parentFile = null) => new(
        id, "/source", "/source/bundle", file, id, 0, type, name, null, 0, 1,
        gameObject, gameObjectFile, parent, parentFile);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
