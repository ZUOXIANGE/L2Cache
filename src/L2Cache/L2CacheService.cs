using L2Cache.Abstractions;
using L2Cache.Abstractions.Serialization;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Configuration;
using L2Cache.Internal;
using L2Cache.Serializers.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace L2Cache;

/// <summary>
/// L2Cache 服务的默认通用实现。
/// <para>
/// 该类提供了标准的 L1 (本地内存) + L2 (Redis) 缓存实现。
/// 可以直接用于简单的 Key-Value 缓存场景，也可以被继承以实现特定的数据源加载逻辑 (Cache-Aside 模式)。
/// </para>
/// </summary>
/// <typeparam name="TKey">缓存键的类型。</typeparam>
/// <typeparam name="TValue">缓存值的类型。</typeparam>
public class L2CacheService<TKey, TValue> : AbstractCacheService<TKey, TValue>, ICacheRefreshable<TKey> where TKey : notnull
{
    private readonly IDatabase? _redisDatabase;
    private readonly IMemoryCache? _localCache;
    private readonly ICacheSerializer _serializer;
    private readonly ILogger _logger;
    private readonly ITelemetryProvider? _telemetryProvider;
    private readonly L2CacheOptions _options;
    private readonly string _cacheName;
    private readonly CacheKeyTracker<TKey, TValue>? _keyTracker;
    private readonly ICacheRefreshPolicy<TKey, TValue>? _refreshPolicy;

    #region Constructor

    /// <summary>
    /// 初始化 L2CacheService 的新实例。
    /// </summary>
    public L2CacheService(
        IServiceProvider serviceProvider,
        IOptions<L2CacheOptions> options,
        ILogger<L2CacheService<TKey, TValue>> logger,
        ICacheSerializer? serializer = null,
        CacheKeyTracker<TKey, TValue>? keyTracker = null,
        ICacheRefreshPolicy<TKey, TValue>? refreshPolicy = null)
    {
        _options = options.Value;
        _logger = logger;
        _serializer = serializer ?? new JsonCacheSerializer();
        _telemetryProvider = serviceProvider.GetService(typeof(ITelemetryProvider)) as ITelemetryProvider;
        
        // 默认使用 Value 类型的名称作为缓存区域名称
        _cacheName = typeof(TValue).Name;
        
        // 获取后台刷新相关的服务（如果已注册）
        _keyTracker = keyTracker ?? serviceProvider.GetService(typeof(CacheKeyTracker<TKey, TValue>)) as CacheKeyTracker<TKey, TValue>;
        _refreshPolicy = refreshPolicy ?? serviceProvider.GetService(typeof(ICacheRefreshPolicy<TKey, TValue>)) as ICacheRefreshPolicy<TKey, TValue>;

        // 解析 Redis 数据库实例
        if (_options.UseRedis)
        {
            // 尝试获取 IDatabase。如果未注册，_redisDatabase 将为 null。
            // AbstractCacheService 在 GetRedisDatabase() 返回 null 时会跳过 Redis 操作。
            _redisDatabase = serviceProvider.GetService(typeof(IDatabase)) as IDatabase;
                
            if (_redisDatabase == null)
            {
                _logger.LogWarning("配置中启用了 Redis (UseRedis=true)，但容器中未注册 IDatabase 服务。Redis 缓存功能将不可用。");
            }
        }
            
        // 解析本地缓存实例
        if (_options.UseLocalCache)
        {
            _localCache = serviceProvider.GetService(typeof(IMemoryCache)) as IMemoryCache;
            if (_localCache == null)
            {
                _logger.LogWarning("配置中启用了本地缓存 (UseLocalCache=true)，但容器中未注册 IMemoryCache 服务。本地缓存功能将不可用。");
            }
        }
    }

    #endregion

    #region AbstractCacheService Implementation (Dependencies)

    public override string GetCacheName() => _cacheName;

    protected override IDatabase? GetRedisDatabase() => _redisDatabase;

    protected override IMemoryCache? GetLocalCache() => _localCache;
        
    protected override ICacheSerializer GetCacheSerializer() => _serializer;

    protected override ILogger GetLogger() => _logger;

    protected override ITelemetryProvider? GetTelemetryProvider() => _telemetryProvider;

    protected override L2CacheOptions GetOptions() => _options;

    /// <summary>
    /// 构建业务 Key 的字符串表示。
    /// <para>如果是简单类型（string, ValueType）直接调用 ToString()。对于复杂 DTO 类型，建议重写此方法以生成唯一的 Key。</para>
    /// </summary>
    /// <param name="key">业务 Key 对象。</param>
    /// <returns>Key 的字符串表示。</returns>
    /// <exception cref="InvalidOperationException">当 Key 为复杂类型且未重写此方法时抛出。</exception>
    public override string BuildCacheKey(TKey key)
    {
        if (key is string || key is ValueType)
        {
            return key.ToString() ?? string.Empty;
        }
        throw new InvalidOperationException($"key为自定义DTO，请自行构建缓存key {key}");
    }

    #endregion

    #region Data Source Operations (Default Implementation)

    /// <summary>
    /// 回源查询数据。
    /// <para>当 L1 和 L2 缓存都未命中时，会调用此方法从数据库或其他数据源加载数据。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>数据对象。如果数据不存在返回 null。</returns>
    protected override Task<TValue?> QueryDataAsync(TKey key)
    {
        // 默认实现返回 null，即不进行回源
        return Task.FromResult<TValue?>(default);
    }

    #endregion

    #region Background Refresh Logic

    /// <summary>
    /// 当数据写入本地缓存时触发。
    /// <para>用于注册后台刷新任务。</para>
    /// </summary>
    protected override void OnLocalCacheSet(TKey key, TValue value)
    {
        if (_options.BackgroundRefresh.Enabled && _keyTracker != null)
        {
            var interval = _refreshPolicy?.GetRefreshInterval(key) ?? _options.BackgroundRefresh.Interval;
            _keyTracker.Track(key, interval);
        }
    }

    /// <summary>
    /// 创建本地缓存条目配置选项。
    /// <para>添加了缓存驱逐回调，用于清理后台刷新任务。</para>
    /// </summary>
    protected override MemoryCacheEntryOptions CreateLocalCacheEntryOptions(TKey key, TimeSpan? redisExpiry = null)
    {
        var options = base.CreateLocalCacheEntryOptions(key, redisExpiry);
        if (_options.BackgroundRefresh.Enabled)
        {
            options.RegisterPostEvictionCallback(OnEviction, key);
        }
        return options;
    }

    /// <summary>
    /// 本地缓存驱逐回调。
    /// </summary>
    private void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        // 如果是因为被替换（Replaced），说明缓存还在，不需要停止跟踪
        if (reason == EvictionReason.Replaced)
        {
            return;
        }

        // 只有当缓存真正失效或被移除时，才停止跟踪
        if (state is TKey tKey && _keyTracker != null)
        {
            _keyTracker.Untrack(tKey);
        }
    }

    /// <summary>
    /// 执行后台刷新逻辑。
    /// <para>此方法通常由 BackgroundService 调用。</para>
    /// </summary>
    public async Task RefreshKeyAsync(TKey key)
    {
        var fullKey = GetFullKey(key);
        
        // 1. 检查本地缓存是否仍然存在 (Double Check)
        // 如果本地缓存已经消失，说明不再需要刷新（或者是已经被淘汰了）
        // 注意：使用 object 接收，以兼容 NullValObj (当 TValue 不为 object 时，TryGetValue<TValue> 会失败)
        if (_localCache == null || !_localCache.TryGetValue(fullKey, out object? localObj))
        {
            _keyTracker?.Untrack(key);
            return;
        }

        TValue? newValue = default;

        // 2. 尝试从 Redis 获取最新值
        if (_redisDatabase != null)
        {
            var value = await _redisDatabase.StringGetAsync(fullKey);
            if (value.HasValue)
            {
                if (value == NullValString)
                {
                    newValue = default;
                }
                else
                {
                    newValue = _serializer.DeserializeFromString<TValue>(value!);
                }
            }
        }

        // 3. 如果 Redis 中没有 (或 Redis 未启用)，或者 Redis 中是空值，尝试从数据源加载
        // 注意：如果 Redis 中缓存的是空值 (NullValString)，newValue 为 default (null)，这里也会触发回源。
        // 这是符合预期的设计：后台刷新任务 (Refresh) 的目的就是保持数据的"新鲜度"。
        // 对于空值缓存，我们需要定期确认数据源中是否仍然为空，或者是否有新数据产生。
        if (newValue == null)
        {
            newValue = await QueryDataAsync(key);
        }

        // 4. 更新缓存或移除
        if (newValue != null)
        {
             // 覆写缓存以确保新鲜度，这会重置过期时间并保持 KeyTracker 活跃
             await PutAsync(key, newValue);
             
             // 更新下一次刷新时间
             _keyTracker?.UpdateNextRefresh(key);
        }
        else
        {
            // 数据源返回 null
            if (_options.CacheNullValues)
            {
                // 如果开启了空值缓存，则更新空值缓存
                await PutAsync(key, default!, _options.NullValueExpiry);
                _keyTracker?.UpdateNextRefresh(key);
            }
            else
            {
                // 数据源和 L2 中都不存在了，说明数据已被物理删除，应从 L1 中移除并停止跟踪
                await EvictAsync(key);
            }
        }
    }

    #endregion
}
