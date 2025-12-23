using L2Cache.Abstractions;
using L2Cache.Configuration;
using Microsoft.Extensions.Options;

namespace L2Cache.Internal;

public class DefaultCacheRefreshPolicy<TKey, TValue> : ICacheRefreshPolicy<TKey, TValue> where TKey : notnull
{
    private readonly L2CacheOptions _options;

    public DefaultCacheRefreshPolicy(IOptions<L2CacheOptions> options)
    {
        _options = options.Value;
    }

    public TimeSpan? GetRefreshInterval(TKey key)
    {
        // 默认返回全局配置的间隔
        return _options.BackgroundRefresh.Interval;
    }
}
