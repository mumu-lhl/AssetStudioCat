using AssetStudio.AppCore.Caching;

namespace AssetStudio.AppCore.Tests.Caching;

public sealed class MemoryPreviewCacheTests
{
    [Fact]
    public void EvictsLeastRecentlyUsedItemsAtByteLimit()
    {
        var cache = new MemoryPreviewCache(1);
        cache.Set("first", new byte[600_000]);
        cache.Set("second", new byte[300_000]);
        Assert.True(cache.TryGet("first", out _));

        cache.Set("third", new byte[300_000]);

        Assert.True(cache.TryGet("first", out _));
        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("third", out _));
        Assert.True(cache.CurrentBytes <= 1024 * 1024);
    }

    [Fact]
    public void DoesNotCacheAnItemLargerThanTheLimit()
    {
        var cache = new MemoryPreviewCache(1);
        cache.Set("large", new byte[2 * 1024 * 1024]);

        Assert.False(cache.TryGet("large", out _));
        Assert.Equal(0, cache.CurrentBytes);
    }
}
