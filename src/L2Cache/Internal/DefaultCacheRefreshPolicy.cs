using L2Cache.Abstractions;
using L2Cache.Configuration;
using L2Cache.Core;

namespace L2Cache.Internal;

/// <summary>
/// 默认刷新策略：所有 Key 使用区域配置的刷新间隔。
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
public class DefaultCacheRefreshPolicy<TKey, TValue> : ICacheRefreshPolicy<TKey, TValue> where TKey : notnull
{
    private readonly CacheDescriptor<TKey, TValue> _descriptor;

    public DefaultCacheRefreshPolicy(CacheDescriptor<TKey, TValue> descriptor)
    {
        _descriptor = descriptor;
    }

    public TimeSpan? GetRefreshInterval(TKey key)
    {
        return _descriptor.BackgroundRefresh.Enabled
            ? _descriptor.BackgroundRefresh.Interval
            : null;
    }
}
