using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class ContainerHierarchyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetStudioCat.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildsHierarchicalDirectoriesAndContainers()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "bundle"), "data");
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), CreateSampleEntries());

        var roots = await new ContainerHierarchyService().BuildAsync(index);

        Assert.Equal(4, roots.Count);

        // Root 0: "AllAssets"
        var allAssets = roots.First(r => r.Name == "AllAssets");
        Assert.Equal(6, allAssets.TotalAssetCount);

        // Root 1: "assets"
        var assetsDir = roots.First(r => r.Name == "assets");
        Assert.True(assetsDir.IsDirectory);
        Assert.Equal(4, assetsDir.TotalAssetCount);

        var charactersDir = Assert.Single(assetsDir.Children, c => c.Name == "characters");
        Assert.True(charactersDir.IsDirectory);
        Assert.Equal(4, charactersDir.TotalAssetCount);

        var mikuDir = Assert.Single(charactersDir.Children, c => c.Name == "miku");
        Assert.Equal(3, mikuDir.TotalAssetCount);

        var texturesDir = Assert.Single(mikuDir.Children, c => c.Name == "textures");
        Assert.Equal(2, texturesDir.TotalAssetCount);
        Assert.Contains(texturesDir.Children, c => c.Name == "body.png" && c.TotalAssetCount == 1);
        Assert.Contains(texturesDir.Children, c => c.Name == "face.png" && c.TotalAssetCount == 1);

        var modelsDir = Assert.Single(mikuDir.Children, c => c.Name == "models");
        Assert.Equal(1, modelsDir.TotalAssetCount);
        var mikuPrefab = Assert.Single(modelsDir.Children, c => c.Name == "miku.prefab");
        Assert.Equal(1, mikuPrefab.TotalAssetCount);
        Assert.Empty(mikuPrefab.Children);

        // Root 2: "sound"
        var soundDir = roots.First(r => r.Name == "sound");
        Assert.Equal(1, soundDir.TotalAssetCount);
        var bgmDir = Assert.Single(soundDir.Children, c => c.Name == "bgm");
        var titleMp3 = Assert.Single(bgmDir.Children, c => c.Name == "title.mp3");
        Assert.Equal(1, titleMp3.TotalAssetCount);
        Assert.Empty(titleMp3.Children);

        // Root 3: "(No Container)"
        var noContainerDir = roots.First(r => r.Name == "(No Container)");
        Assert.Equal(1, noContainerDir.TotalAssetCount);
        Assert.Empty(noContainerDir.Children);
    }

    private static async IAsyncEnumerable<AssetIndexEntry> CreateSampleEntries()
    {
        const string file = "/virtual/game.assets";
        yield return CreateEntry(1, "Texture2D", "miku_body", "assets/characters/miku/textures/body.png", file);
        yield return CreateEntry(2, "Texture2D", "miku_face", "assets/characters/miku/textures/face.png", file);
        yield return CreateEntry(3, "Mesh", "miku_mesh", "assets/characters/miku/models/miku.prefab", file);
        yield return CreateEntry(4, "Texture2D", "rin_body", "assets/characters/rin/textures/body.png", file);
        yield return CreateEntry(5, "AudioClip", "bgm_title", "sound/bgm/title.mp3", file);
        await Task.Yield();
        yield return CreateEntry(6, "Shader", "loose_shader", null, file);
    }

    private static AssetIndexEntry CreateEntry(
        long id,
        string type,
        string name,
        string? container,
        string file) => new(
        id, "/source", "/source/bundle", file, id, 0, type, name, container, 0, 1024);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
