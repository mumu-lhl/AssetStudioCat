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

    [Fact]
    public async Task ResolvesOnlyBundlesContainingRequestedSerializedFiles()
    {
        var source = CreateSourceDirectory();
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), DependencyEntries());

        var sources = await index.ResolveObjectSourcesAsync(["shared.assets"]);

        var sourcePath = Assert.Single(sources);
        Assert.Equal("/source/bundle-shared", sourcePath);
    }

    [Fact]
    public async Task EnumeratesFilteredRowsAndCountsTypes()
    {
        var source = CreateSourceDirectory();
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries(20));

        var filtered = new List<AssetIndexEntry>();
        await foreach (var entry in index.EnumerateAsync("group-1", "TextAsset"))
        {
            filtered.Add(entry);
        }
        var counts = await index.GetTypeCountsAsync();

        Assert.All(filtered, entry => Assert.Equal("TextAsset", entry.TypeName));
        Assert.Equal(10, counts["Texture2D"]);
        Assert.Equal(10, counts["TextAsset"]);
    }

    [Fact]
    public void FileSelectionsHaveStableDistinctCacheKeys()
    {
        var source = CreateSourceDirectory();
        var first = Path.Combine(source, "bundle-a");
        var second = Path.Combine(source, "bundle-b");
        File.WriteAllText(second, "second");

        var one = AssetSourceFingerprint.CreateFiles([first, second]);
        var reordered = AssetSourceFingerprint.CreateFiles([second, first]);
        var subset = AssetSourceFingerprint.CreateFiles([first]);
        File.AppendAllText(first, "changed");
        var changed = AssetSourceFingerprint.CreateFiles([first, second]);

        Assert.Equal(one.IndexKey, reordered.IndexKey);
        Assert.NotEqual(one.IndexKey, subset.IndexKey);
        Assert.Equal(one.IndexKey, changed.IndexKey);
        Assert.NotEqual(one.Digest, changed.Digest);
    }

    [Fact]
    public async Task GloballySortsPagedResultsWithoutChangingIndexOrder()
    {
        var source = CreateSourceDirectory();
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        await index.BuildAsync(AssetSourceFingerprint.Create(source), Entries(30));

        var page = await index.QueryAsync(new AssetIndexQuery(
            Offset: 5,
            Limit: 5,
            SortField: AssetSortField.Name,
            SortDescending: true));

        var expected = Enumerable.Range(0, 30)
            .Select(value => $"asset-{value}")
            .OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase)
            .Skip(5)
            .Take(5);
        Assert.Equal(expected, page.Items.Select(item => item.Name));
        Assert.Equal(30, page.TotalCount);
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

    private static async IAsyncEnumerable<AssetIndexEntry> DependencyEntries()
    {
        yield return new AssetIndexEntry(
            1, "/source", "/source/bundle-main", "/virtual/main.assets", 1,
            95, "Animator", "character", null, 0, 128);
        await Task.Yield();
        yield return new AssetIndexEntry(
            2, "/source", "/source/bundle-shared", "/virtual/shared.assets", 2,
            43, "Mesh", "body", null, 128, 256);
        yield return new AssetIndexEntry(
            3, "/source", "/source/bundle-other", "/virtual/other.assets", 3,
            28, "Texture2D", "unrelated", null, 384, 64);
    }

    [Fact]
    public async Task ResumeInterruptedBuildCompletesSuccessfully()
    {
        var source = CreateSourceDirectory();
        var fileA = Path.Combine(source, "bundle-a");
        var fileB = Path.Combine(source, "bundle-b");
        File.WriteAllText(fileB, "bundle-b-content");

        var sourceFiles = new[] { fileA, fileB };
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);

        // Step 1: Simulate interrupted build by indexing only fileA and not finalizing
        var initialState = await index.CheckResumeStateAsync(sourceFiles);
        var appender = await index.CreateAppenderAsync(initialState);
        var entryA = new AssetIndexEntry(
            0, source, fileA, "data.assets", 1, 28, "Texture2D", "asset-a", "container-a", 0, 100);
        appender.Containers["data.assets:1"] = "container-a";
        appender.AppendEntry(entryA);
        await appender.CommitBatchAsync([fileA], 1);
        await appender.DisposeAsync();

        // Step 2: Check resume state - should see fileA indexed, fileB remaining
        var resumeState = await index.CheckResumeStateAsync(sourceFiles);
        Assert.True(resumeState.CanResume);
        Assert.NotNull(resumeState.Checkpoint);
        Assert.True(resumeState.Checkpoint.IndexedFiles.ContainsKey(fileA));
        Assert.Single(resumeState.RemainingFiles);
        Assert.Equal(fileB, resumeState.RemainingFiles[0]);
        Assert.Equal(1, resumeState.ExistingEntryCount);

        // Step 3: Resume by appending fileB and finalizing
        var resumeAppender = await index.CreateAppenderAsync(resumeState);
        Assert.Equal(1, resumeAppender.CurrentEntryCount);
        var entryB = new AssetIndexEntry(
            1, source, fileB, "data2.assets", 2, 49, "TextAsset", "asset-b", "container-b", 0, 200);
        resumeAppender.Containers["data2.assets:2"] = "container-b";
        resumeAppender.AppendEntry(entryB);
        await resumeAppender.CommitBatchAsync([fileB], 1);
        await resumeAppender.FinalizeAsync(AssetSourceFingerprint.CreateFiles(sourceFiles));
        await resumeAppender.DisposeAsync();

        // Step 4: Verify index validity and query results
        var fingerprint = AssetSourceFingerprint.CreateFiles(sourceFiles);
        Assert.True(await index.IsCurrentAsync(fingerprint));

        var queryResult = await index.QueryAsync(new AssetIndexQuery(Limit: 10));
        Assert.Equal(2, queryResult.TotalCount);
        Assert.Equal(2, queryResult.Items.Count);
        Assert.Equal("asset-a", queryResult.Items[0].Name);
        Assert.Equal("asset-b", queryResult.Items[1].Name);
        Assert.Equal(0, queryResult.Items[0].Id);
        Assert.Equal(1, queryResult.Items[1].Id);
    }

    [Fact]
    public async Task IncrementalUpdateAppendsNewSourceFiles()
    {
        var source = CreateSourceDirectory();
        var fileA = Path.Combine(source, "bundle-a");
        var fileB = Path.Combine(source, "bundle-b");

        // Initial build with only fileA
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        var initialAppender = await index.CreateAppenderAsync(new IndexResumeState(false, null, [fileA], 0));
        initialAppender.AppendEntry(new AssetIndexEntry(0, source, fileA, "data.assets", 1, 28, "Texture2D", "asset-a", null, 0, 100));
        await initialAppender.CommitBatchAsync([fileA], 1);
        await initialAppender.FinalizeAsync(AssetSourceFingerprint.CreateFiles([fileA]));
        await initialAppender.DisposeAsync();

        // Add fileB to the folder
        File.WriteAllText(fileB, "bundle-b-content");
        var allFiles = new[] { fileA, fileB };

        // Verify incremental resume state recognizes fileA as indexed and fileB as remaining
        var resumeState = await index.CheckResumeStateAsync(allFiles);
        Assert.True(resumeState.CanResume);
        Assert.NotNull(resumeState.Checkpoint);
        Assert.True(resumeState.Checkpoint.IndexedFiles.ContainsKey(fileA));
        Assert.Single(resumeState.RemainingFiles);
        Assert.Equal(fileB, resumeState.RemainingFiles[0]);

        // Append fileB
        var appender = await index.CreateAppenderAsync(resumeState);
        Assert.Equal(1, appender.CurrentEntryCount);
        appender.AppendEntry(new AssetIndexEntry(1, source, fileB, "data.assets", 2, 49, "TextAsset", "asset-b", null, 0, 50));
        await appender.CommitBatchAsync([fileB], 1);
        await appender.FinalizeAsync(AssetSourceFingerprint.CreateFiles(allFiles));
        await appender.DisposeAsync();

        // Verify query returns both assets
        var page = await index.QueryAsync(new AssetIndexQuery(Limit: 10));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["asset-a", "asset-b"], page.Items.Select(x => x.Name));
    }

    [Fact]
    public async Task ModifiedSourceFileInvalidatesResume()
    {
        var source = CreateSourceDirectory();
        var fileA = Path.Combine(source, "bundle-a");
        var fileB = Path.Combine(source, "bundle-b");
        File.WriteAllText(fileB, "bundle-b-content");

        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);
        var initialAppender = await index.CreateAppenderAsync(new IndexResumeState(false, null, [fileA], 0));
        initialAppender.AppendEntry(new AssetIndexEntry(0, source, fileA, "data.assets", 1, 28, "Texture2D", "asset-a", null, 0, 100));
        await initialAppender.CommitBatchAsync([fileA], 1);
        await initialAppender.FinalizeAsync(AssetSourceFingerprint.CreateFiles([fileA]));
        await initialAppender.DisposeAsync();

        // Wait a tick or modify fileA content
        File.AppendAllText(fileA, "modified-content");

        // CheckResumeState should fail resume
        var resumeState = await index.CheckResumeStateAsync([fileA, fileB]);
        Assert.False(resumeState.CanResume);
    }

    [Fact]
    public async Task CorruptedUncommittedBatchIsRolledBackOnResume()
    {
        var source = CreateSourceDirectory();
        var fileA = Path.Combine(source, "bundle-a");
        var fileB = Path.Combine(source, "bundle-b");
        File.WriteAllText(fileB, "bundle-b-content");

        var allFiles = new[] { fileA, fileB };
        var index = new DiskAssetIndex(Path.Combine(_root, "indexes"), source);

        // Commit fileA
        var initialAppender = await index.CreateAppenderAsync(new IndexResumeState(false, null, allFiles.ToList(), 0));
        initialAppender.AppendEntry(new AssetIndexEntry(0, source, fileA, "data.assets", 1, 28, "Texture2D", "asset-a", null, 0, 100));
        await initialAppender.CommitBatchAsync([fileA], 1);
        await initialAppender.DisposeAsync();

        // Simulate a crash that wrote corrupt uncommitted partial bytes to assets.jsonl & assets.offsets
        var rowsPath = Path.Combine(index.DirectoryPath, "assets.jsonl");
        var offsetsPath = Path.Combine(index.DirectoryPath, "assets.offsets");
        await File.AppendAllTextAsync(rowsPath, "{ corrupt partial json\n");
        await File.AppendAllTextAsync(offsetsPath, "corrupt");

        // Now resume: CreateAppenderAsync should rollback to checkpoint
        var resumeState = await index.CheckResumeStateAsync(allFiles);
        Assert.True(resumeState.CanResume);
        var resumeAppender = await index.CreateAppenderAsync(resumeState);
        Assert.Equal(1, resumeAppender.CurrentEntryCount);

        // Append fileB and finalize
        resumeAppender.AppendEntry(new AssetIndexEntry(1, source, fileB, "data.assets", 2, 49, "TextAsset", "asset-b", null, 0, 50));
        await resumeAppender.CommitBatchAsync([fileB], 1);
        await resumeAppender.FinalizeAsync(AssetSourceFingerprint.CreateFiles(allFiles));
        await resumeAppender.DisposeAsync();

        // Check that querying works without any JSON parse errors and has exactly 2 entries
        var page = await index.QueryAsync(new AssetIndexQuery(Limit: 10));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("asset-a", page.Items[0].Name);
        Assert.Equal("asset-b", page.Items[1].Name);
    }

    [Fact]
    public async Task GetTypeCountsAndFirstPageUseMetadataAndOffsetFastPath()
    {
        var source = CreateSourceDirectory();
        var fingerprint = AssetSourceFingerprint.Create(source);
        var indexesDir = Path.Combine(_root, "indexes");
        var index1 = new DiskAssetIndex(indexesDir, source);
        await index1.BuildAsync(fingerprint, Entries(500));

        // Create a completely fresh DiskAssetIndex instance (no in-memory cache)
        var index2 = new DiskAssetIndex(indexesDir, source);
        Assert.True(await index2.IsCurrentAsync(fingerprint));

        // 1. Type counts should return from metadata / type_counts.json without loading all rows
        var typeCounts = await index2.GetTypeCountsAsync();
        Assert.Equal(250, typeCounts["Texture2D"]);
        Assert.Equal(250, typeCounts["TextAsset"]);

        // 2. Unfiltered first page query should return via fast-path offsets
        var page = await index2.QueryAsync(new AssetIndexQuery(Offset: 0, Limit: 50));
        Assert.Equal(500, page.TotalCount);
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(50, page.NextOffset);
        Assert.Equal(0, page.Items[0].Id);
        Assert.Equal(49, page.Items[^1].Id);

        // 3. Second page offset query should also work via fast-path
        var page2 = await index2.QueryAsync(new AssetIndexQuery(Offset: 50, Limit: 50));
        Assert.Equal(500, page2.TotalCount);
        Assert.Equal(50, page2.Items.Count);
        Assert.Equal(100, page2.NextOffset);
        Assert.Equal(50, page2.Items[0].Id);
        Assert.Equal(99, page2.Items[^1].Id);

        // 4. Filtered query should load entries and filter correctly
        var filteredPage = await index2.QueryAsync(new AssetIndexQuery(Limit: 50, TypeName: "Texture2D"));
        Assert.Equal(250, filteredPage.TotalCount);
        Assert.Equal(50, filteredPage.Items.Count);
        Assert.All(filteredPage.Items, item => Assert.Equal("Texture2D", item.TypeName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
