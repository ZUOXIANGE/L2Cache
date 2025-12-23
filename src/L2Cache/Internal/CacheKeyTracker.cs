using System.Collections.Concurrent;

namespace L2Cache.Internal;

public class CacheKeyTracker<TKey, TValue> where TKey : notnull
{
    private class RefreshEntry
    {
        public DateTimeOffset NextRefresh { get; set; }
        public TimeSpan Interval { get; set; }
    }

    private readonly ConcurrentDictionary<TKey, RefreshEntry> _entries = new();

    public bool IsEnabled { get; set; } = false;

    public void Track(TKey key, TimeSpan interval)
    {
        if (!IsEnabled) return;
        
        var entry = new RefreshEntry
        {
            Interval = interval,
            NextRefresh = DateTimeOffset.UtcNow.Add(interval)
        };

        _entries.AddOrUpdate(key, entry, (_, _) => entry);
    }

    public void Untrack(TKey key)
    {
        _entries.TryRemove(key, out _);
    }

    /// <summary>
    /// 获取需要刷新的Key
    /// </summary>
    public IEnumerable<TKey> GetDueKeys()
    {
        var now = DateTimeOffset.UtcNow;
        var dueKeys = new List<TKey>();

        foreach (var kvp in _entries)
        {
            if (kvp.Value.NextRefresh <= now)
            {
                dueKeys.Add(kvp.Key);
            }
        }

        return dueKeys;
    }

    /// <summary>
    /// 更新Key的下一次刷新时间
    /// </summary>
    public void UpdateNextRefresh(TKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.NextRefresh = DateTimeOffset.UtcNow.Add(entry.Interval);
        }
    }
}
