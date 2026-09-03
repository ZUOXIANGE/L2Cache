using L2Cache.Abstractions.Stores;
using Microsoft.Extensions.Caching.Memory;

namespace L2Cache.Stores;

/// <summary>
/// 基于 <see cref="IMemoryCache"/> 的 L1 本地存储实现。
/// <para>
/// 存储"已反序列化的对象实例"，命中后无需再次反序列化。
/// null 值以内部哨兵标记存储，用于支持空值缓存（防穿透）。
/// </para>
/// </summary>
internal sealed class MemoryCacheStore : IL1CacheStore
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultTtl;

    public MemoryCacheStore(IMemoryCache cache)
    {
        _cache = cache;
        _defaultTtl = TimeSpan.FromHours(1);
    }

    public L1Entry GetValue(string key)
    {
        if (_cache.TryGetValue(key, out object? value))
        {
            return value == NullMarker
                ? new L1Entry(true, IsNullValue: true, Value: null)
                : new L1Entry(true, IsNullValue: false, value);
        }

        return L1Entry.NotFound;
    }

    public void SetValue(string key, object? value, TimeSpan? ttl)
    {
        // 直接使用 TimeSpan 重载，避免每次写入分配 MemoryCacheEntryOptions
        _cache.Set(key, value ?? NullMarker, ttl ?? _defaultTtl);
    }

    public void Remove(string key) => _cache.Remove(key);

    public bool Exists(string key) => _cache.TryGetValue(key, out _);

    /// <summary>空值哨兵。仅存在于本存储内部，不会外泄。</summary>
    private static readonly object NullMarker = new();
}
