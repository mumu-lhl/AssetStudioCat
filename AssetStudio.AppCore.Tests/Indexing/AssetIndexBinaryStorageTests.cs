using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Indexing;

public sealed class AssetIndexBinaryStorageTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "AssetStudioCat.BinaryTests", Guid.NewGuid().ToString("N"));

    public AssetIndexBinaryStorageTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task RoundTripEntriesPreservesAllPropertiesExact()
    {
        var entries = new List<AssetIndexEntry>
        {
            new(
                Id: 0,
                SourcePath: "/path/to/game",
                ObjectSourcePath: "/path/to/game/bundle1",
                SerializedFile: "data.assets",
                PathId: -5827622400517570169,
                ClassId: 28,
                TypeName: "Texture2D",
                Name: "test_tex_1",
                Container: "assets/textures/tex1.png",
                ByteStart: 1024,
                ByteSize: 2048,
                GameObjectPathId: -100,
                GameObjectSerializedFile: "main.assets",
                ParentTransformPathId: -200,
                ParentTransformSerializedFile: "parent.assets"),
            new(
                Id: 1,
                SourcePath: "/path/to/game",
                ObjectSourcePath: "/path/to/game/bundle2",
                SerializedFile: "shared.assets",
                PathId: 123456789012345,
                ClassId: 49,
                TypeName: "TextAsset",
                Name: "dialogue",
                Container: null,
                ByteStart: 4096,
                ByteSize: 512,
                GameObjectPathId: null,
                GameObjectSerializedFile: null,
                ParentTransformPathId: null,
                ParentTransformSerializedFile: null)
        };

        var binPath = Path.Combine(_testDir, "assets.bin");
        await AssetIndexBinaryStorage.SaveAsync(binPath, entries);

        Assert.True(File.Exists(binPath));

        var loaded = await AssetIndexBinaryStorage.TryLoadAsync(binPath, entries.Count);
        Assert.NotNull(loaded);
        Assert.Equal(entries.Count, loaded.Length);

        for (int i = 0; i < entries.Count; i++)
        {
            var exp = entries[i];
            var act = loaded[i];
            Assert.Equal(exp.Id, act.Id);
            Assert.Equal(exp.SourcePath, act.SourcePath);
            Assert.Equal(exp.ObjectSourcePath, act.ObjectSourcePath);
            Assert.Equal(exp.SerializedFile, act.SerializedFile);
            Assert.Equal(exp.PathId, act.PathId);
            Assert.Equal(exp.ClassId, act.ClassId);
            Assert.Equal(exp.TypeName, act.TypeName);
            Assert.Equal(exp.Name, act.Name);
            Assert.Equal(exp.Container, act.Container);
            Assert.Equal(exp.ByteStart, act.ByteStart);
            Assert.Equal(exp.ByteSize, act.ByteSize);
            Assert.Equal(exp.GameObjectPathId, act.GameObjectPathId);
            Assert.Equal(exp.GameObjectSerializedFile, act.GameObjectSerializedFile);
            Assert.Equal(exp.ParentTransformPathId, act.ParentTransformPathId);
            Assert.Equal(exp.ParentTransformSerializedFile, act.ParentTransformSerializedFile);
        }
    }

    [Fact]
    public async Task TryLoadReturnsNullOnInvalidOrMismatchedCount()
    {
        var binPath = Path.Combine(_testDir, "assets.bin");
        var entries = new List<AssetIndexEntry>
        {
            new(0, "a", "b", "c", 1, 2, "d", "e", null, 0, 100)
        };

        await AssetIndexBinaryStorage.SaveAsync(binPath, entries);

        // Mismatched expected entry count
        var loadedMismatch = await AssetIndexBinaryStorage.TryLoadAsync(binPath, expectedEntryCount: 999);
        Assert.Null(loadedMismatch);

        // Corrupted magic bytes
        var bytes = await File.ReadAllBytesAsync(binPath);
        bytes[0] = 0xFF;
        await File.WriteAllBytesAsync(binPath, bytes);

        var loadedCorrupt = await AssetIndexBinaryStorage.TryLoadAsync(binPath, expectedEntryCount: 1);
        Assert.Null(loadedCorrupt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
}
