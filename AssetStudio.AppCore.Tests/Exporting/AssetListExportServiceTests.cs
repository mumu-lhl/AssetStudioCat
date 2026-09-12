using AssetStudio.AppCore.Exporting;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Tests.Exporting;

public sealed class AssetListExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"assetstudio-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task StreamsFilteredCsvAndJsonFromDiskIndex()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries());
        var service = new AssetListExportService();

        var csv = Path.Combine(_root, "assets.csv");
        var json = Path.Combine(_root, "assets.json");
        Assert.Equal(1, await service.ExportAsync(index, csv, AssetListExportFormat.Csv, typeName: "Texture2D"));
        Assert.Equal(2, await service.ExportAsync(index, json, AssetListExportFormat.Json));

        Assert.Contains("Texture, Name", await File.ReadAllTextAsync(csv));
        var parsed = await File.ReadAllTextAsync(json);
        Assert.Contains("Texture2D", parsed);
        Assert.Contains("AudioClip", parsed);
    }

    private static async IAsyncEnumerable<AssetIndexEntry> Entries()
    {
        yield return new AssetIndexEntry(0, "source", "bundle", "file", 1, 28, "Texture2D", "Texture, Name", "a/path", 0, 10);
        await Task.Yield();
        yield return new AssetIndexEntry(1, "source", "bundle", "file", 2, 83, "AudioClip", "Audio", null, 10, 20);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
