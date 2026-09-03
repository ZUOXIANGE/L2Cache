using L2Cache.Abstractions.Policies;
using L2Cache.Abstractions.Serialization;
using L2Cache.Configuration;
using L2Cache.Policies;
using L2Cache.Serializers.Json;
using Microsoft.Extensions.DependencyInjection;

namespace L2Cache.Core;

/// <summary>
/// 缓存区域的运行时描述符：区域名、配置与冻结的策略实例。
/// <para>由 <c>AddCache&lt;TKey,TValue&gt;</c> 注册为单例，进程内每个区域一份。</para>
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
public sealed class CacheDescriptor<TKey, TValue> where TKey : notnull
{
    /// <summary>区域名称（Redis Key 前缀与失效频道后缀）。</summary>
    public required string CacheName { get; init; }

    /// <summary>区域配置。</summary>
    public required CacheRegionOptions Options { get; init; }

    /// <summary>Key 构建策略。</summary>
    public required IKeyBuilder<TKey> KeyBuilder { get; init; }

    /// <summary>过期策略。</summary>
    public required IExpiryPolicy Expiry { get; init; }

    /// <summary>空值策略。</summary>
    public required INullValuePolicy NullValue { get; init; }

    /// <summary>序列化器。</summary>
    public required ICacheSerializer Serializer { get; init; }

    /// <summary>锁策略。null 表示未配置任何锁（直读直写）。</summary>
    public ILockPolicy? Lock { get; init; }

    /// <summary>后台刷新 Key 跟踪器（仅当区域启用后台刷新时非 null）。</summary>
    internal Internal.CacheKeyTracker<TKey, TValue>? Tracker { get; init; }

    /// <summary>刷新间隔策略（仅当区域启用后台刷新时非 null）。</summary>
    internal Abstractions.ICacheRefreshPolicy<TKey, TValue>? RefreshPolicy { get; init; }

    internal BackgroundRefreshOptions BackgroundRefresh => Options.BackgroundRefresh;

    /// <summary>构建完整缓存 Key："{CacheName}:{Key}"。</summary>
    public string BuildFullKey(TKey key) => $"{CacheName}:{KeyBuilder.Build(key)}";

    /// <summary>构建完整缓存 Key："{CacheName}:{cacheKey}"。</summary>
    public string BuildFullKey(string cacheKey) => $"{CacheName}:{cacheKey}";

    /// <summary>L1 写入有效值后登记后台刷新跟踪。</summary>
    internal void TrackKey(TKey key)
    {
        if (Tracker == null || !Options.BackgroundRefresh.Enabled)
        {
            return;
        }

        var interval = RefreshPolicy?.GetRefreshInterval(key) ?? Options.BackgroundRefresh.Interval;
        Tracker.Track(key, interval);
    }

    /// <summary>Key 被淘汰/移除后解除后台刷新跟踪。</summary>
    internal void UntrackKey(TKey key) => Tracker?.Untrack(key);

    internal static CacheDescriptor<TKey, TValue> Create(
        IServiceProvider serviceProvider,
        L2CacheOptions globalOptions,
        CacheRegionOptions regionOptions)
    {
        var useRedis = globalOptions.UseRedis;
        var l2 = useRedis ? serviceProvider.GetRequiredService<Abstractions.Stores.IL2CacheStore>() : null;

        ILockPolicy? lockPolicy = null;
        if (regionOptions.Lock.EnabledMemoryLock && regionOptions.Lock.EnabledDistributedLock && l2 != null)
        {
            lockPolicy = new ChainedLockPolicy(
                new MemoryLockPolicy(regionOptions.Lock.LockTimeout),
                new DistributedLockPolicy(l2, regionOptions.Lock.LockTimeout, regionOptions.Lock.DistributedLockExpiry));
        }
        else if (regionOptions.Lock.EnabledMemoryLock)
        {
            lockPolicy = new MemoryLockPolicy(regionOptions.Lock.LockTimeout);
        }
        else if (regionOptions.Lock.EnabledDistributedLock && l2 != null)
        {
            lockPolicy = new DistributedLockPolicy(l2, regionOptions.Lock.LockTimeout, regionOptions.Lock.DistributedLockExpiry);
        }

        return new CacheDescriptor<TKey, TValue>
        {
            CacheName = regionOptions.CacheName,
            Options = regionOptions,
            KeyBuilder = serviceProvider.GetService<IKeyBuilder<TKey>>() ?? new DefaultKeyBuilder<TKey>(),
            Expiry = serviceProvider.GetService<IExpiryPolicy>() ?? new DefaultExpiryPolicy(regionOptions),
            NullValue = serviceProvider.GetService<INullValuePolicy>() ?? new SentinelNullValuePolicy(regionOptions.NullValue),
            Serializer = serviceProvider.GetService<ICacheSerializer>() ?? new JsonCacheSerializer(),
            Lock = lockPolicy
        };
    }
}
