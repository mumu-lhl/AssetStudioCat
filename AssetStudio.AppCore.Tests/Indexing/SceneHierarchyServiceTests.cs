using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class SceneHierarchyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetStudioCat.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildsNestedGameObjectsFromTransformReferences()
    {
        var source = Path.Combine(_root, "source1");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "bundle"), "data");
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes1"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries());

        var roots = await new SceneHierarchyService().BuildAsync(index);

        var root = Assert.Single(roots);
        Assert.Equal("Root", root.Name);
        Assert.Equal("Child", Assert.Single(root.Children).Name);
    }

    [Fact]
    public async Task GroupsExpandableNodesFirstAndSortsByName()
    {
        var source = Path.Combine(_root, "source2");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "bundle"), "data");
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes2"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), ComplexEntries());

        var roots = await new SceneHierarchyService().BuildAsync(index);

        // Expected roots:
        // Expandable group: ParentA, ParentB (sorted alphabetically)
        // Non-expandable group: LeafC, LeafD (sorted alphabetically)
        Assert.Equal(4, roots.Count);
        Assert.Equal("ParentA", roots[0].Name);
        Assert.NotEmpty(roots[0].Children);

        Assert.Equal("ParentB", roots[1].Name);
        Assert.NotEmpty(roots[1].Children);

        Assert.Equal("LeafC", roots[2].Name);
        Assert.Empty(roots[2].Children);

        Assert.Equal("LeafD", roots[3].Name);
        Assert.Empty(roots[3].Children);

        // Inside ParentA:
        // Expandable group: SubFolderA, SubFolderZ
        // Non-expandable group: ChildA, ChildZ
        var parentAChildren = roots[0].Children;
        Assert.Equal(4, parentAChildren.Count);
        Assert.Equal("SubFolderA", parentAChildren[0].Name);
        Assert.NotEmpty(parentAChildren[0].Children);

        Assert.Equal("SubFolderZ", parentAChildren[1].Name);
        Assert.NotEmpty(parentAChildren[1].Children);

        Assert.Equal("ChildA", parentAChildren[2].Name);
        Assert.Empty(parentAChildren[2].Children);

        Assert.Equal("ChildZ", parentAChildren[3].Name);
        Assert.Empty(parentAChildren[3].Children);
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

    private static async IAsyncEnumerable<AssetIndexEntry> ComplexEntries()
    {
        const string file = "/virtual/scene.assets";
        // Roots
        yield return Entry(1, "GameObject", "ParentB", file);
        yield return Entry(2, "GameObject", "LeafD", file);
        yield return Entry(3, "GameObject", "ParentA", file);
        yield return Entry(4, "GameObject", "LeafC", file);

        // Children of ParentA
        yield return Entry(5, "GameObject", "ChildZ", file);
        yield return Entry(6, "GameObject", "SubFolderZ", file);
        yield return Entry(7, "GameObject", "ChildA", file);
        yield return Entry(8, "GameObject", "SubFolderA", file);

        // Children of SubFolders
        yield return Entry(9, "GameObject", "Grandchild1", file);
        yield return Entry(10, "GameObject", "Grandchild2", file);

        // Child of ParentB
        yield return Entry(11, "GameObject", "ChildOfB", file);

        // Transforms
        // Roots
        yield return Entry(101, "Transform", "T1", file, 1, file); // ParentB
        yield return Entry(102, "Transform", "T2", file, 2, file); // LeafD
        yield return Entry(103, "Transform", "T3", file, 3, file); // ParentA
        yield return Entry(104, "Transform", "T4", file, 4, file); // LeafC

        // Children of ParentA (T3)
        yield return Entry(105, "Transform", "T5", file, 5, file, 103, file); // ChildZ
        yield return Entry(106, "Transform", "T6", file, 6, file, 103, file); // SubFolderZ
        yield return Entry(107, "Transform", "T7", file, 7, file, 103, file); // ChildA
        yield return Entry(108, "Transform", "T8", file, 8, file, 103, file); // SubFolderA

        // Grandchildren
        yield return Entry(109, "Transform", "T9", file, 9, file, 106, file); // Grandchild of SubFolderZ
        yield return Entry(110, "Transform", "T10", file, 10, file, 108, file); // Grandchild of SubFolderA

        // Child of ParentB (T1)
        yield return Entry(111, "Transform", "T11", file, 11, file, 101, file); // ChildOfB
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

    [Fact]
    public async Task BenchmarkBuildSceneHierarchyOnRealData()
    {
        var dir = "/home/mumulhl/data/bangdream-data/data";
        var cacheRoot = "/home/mumulhl/data/AssetStudioCatData/cache/indexes";
        if (!Directory.Exists(dir) || !Directory.Exists(cacheRoot)) return;

        var fingerprint = AssetSourceFingerprint.Create(dir);
        var index = new DiskAssetIndex(cacheRoot, fingerprint.IndexKey);
        if (!await index.IsCurrentAsync(fingerprint)) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var service = new SceneHierarchyService();
        var roots = await service.BuildAsync(index);
        var time = sw.ElapsedMilliseconds;

        int CountNodes(SceneHierarchyNode n) => 1 + n.Children.Sum(CountNodes);
        int totalNodes = roots.Sum(CountNodes);
        System.Console.WriteLine($"[Benchmark] Scene hierarchy optimized load: {time} ms (roots: {roots.Count}, totalNodes: {totalNodes})");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
