namespace AssetStudio.AppCore.Caching;

public sealed class MemoryPreviewCache
{
    private readonly long _maximumBytes;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _leastRecentlyUsed = [];
    private readonly object _gate = new();
    private long _currentBytes;

    public MemoryPreviewCache(int maximumMegabytes)
    {
        _maximumBytes = Math.Max(1, maximumMegabytes) * 1024L * 1024L;
    }

    public long CurrentBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBytes;
            }
        }
    }

    public bool TryGet(string key, out byte[] value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = [];
                return false;
            }

            _leastRecentlyUsed.Remove(node);
            _leastRecentlyUsed.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            RemoveCore(key);
            if (value.LongLength > _maximumBytes)
            {
                return;
            }

            var node = _leastRecentlyUsed.AddFirst(new CacheEntry(key, value));
            _entries.Add(key, node);
            _currentBytes += value.LongLength;
            while (_currentBytes > _maximumBytes && _leastRecentlyUsed.Last is { } last)
            {
                RemoveCore(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _leastRecentlyUsed.Clear();
            _currentBytes = 0;
        }
    }

    private void RemoveCore(string key)
    {
        if (_entries.Remove(key, out var node))
        {
            _leastRecentlyUsed.Remove(node);
            _currentBytes -= node.Value.Value.LongLength;
        }
    }

    private sealed record CacheEntry(string Key, byte[] Value);
}
