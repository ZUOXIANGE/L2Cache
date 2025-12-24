using System.Diagnostics;
using L2Cache.Abstractions;
using L2Cache.Abstractions.Serialization;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Configuration;
using L2Cache.Internal;
using L2Cache.Logging;
using L2Cache.Serializers.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache;

/// <summary>
/// L2Cache 服务的抽象基类，实现了多级缓存的核心逻辑。
/// <para>
/// 包含 L1（本地内存）和 L2（Redis）缓存的两级架构，支持 Cache-Aside 模式、批量操作和遥测监控。
/// </para>
/// </summary>
/// <typeparam name="TKey">缓存 Key 的类型。</typeparam>
/// <typeparam name="TValue">缓存 Value 的类型。</typeparam>
public abstract class AbstractCacheService<TKey, TValue> : ICacheService<TKey, TValue> where TKey : notnull
{
    protected const string NullValString = "@@NULL@@";
    // ReSharper disable once StaticMemberInGenericType
    protected static readonly object NullValObj = new object();

    private readonly AsyncKeyedLocker<TKey> _memoryLocker = new();

    /// <summary>
    /// 内部缓存查询结果状态
    /// </summary>
    protected enum CacheStatus
    {
        Found,
        FoundNull,
        NotFound
    }

    #region 1. 基础配置与工具 (Infrastructure & Tools)

    /// <summary>
    /// 获取缓存名称。
    /// <para>通常用于 Redis Key 的前缀，或在日志和遥测中作为标识。</para>
    /// </summary>
    /// <returns>缓存名称字符串。</returns>
    public abstract string GetCacheName();

    /// <summary>
    /// 获取缓存配置选项。
    /// <para>用于控制锁策略等行为。</para>
    /// </summary>
    /// <returns>缓存配置选项对象。</returns>
    protected virtual L2CacheOptions GetOptions() => new L2CacheOptions();

    /// <summary>
    /// 获取缓存序列化器。
    /// <para>默认使用 JSON 序列化器 (<see cref="JsonCacheSerializer"/>)。</para>
    /// </summary>
    /// <returns>缓存序列化器实例。</returns>
    protected virtual ICacheSerializer GetCacheSerializer() => new JsonCacheSerializer();

    /// <summary>
    /// 获取遥测提供者。
    /// <para>用于记录分布式追踪（Activity）和指标（Metrics）。默认为 null（不记录）。</para>
    /// </summary>
    /// <returns>遥测提供者实例，如果未启用则返回 null。</returns>
    protected virtual ITelemetryProvider? GetTelemetryProvider() => null;

    /// <summary>
    /// 获取日志记录器。
    /// <para>用于记录缓存操作的日志（命中、未命中、错误等）。默认为 null。</para>
    /// </summary>
    /// <returns>日志记录器实例，如果未启用则返回 null。</returns>
    protected virtual ILogger? GetLogger() => null;

    /// <summary>
    /// 构建业务 Key 的字符串表示。
    /// <para>如果是简单类型（string, ValueType）直接调用 ToString()。对于复杂 DTO 类型，建议重写此方法以生成唯一的 Key。</para>
    /// </summary>
    /// <param name="key">业务 Key 对象。</param>
    /// <returns>Key 的字符串表示。</returns>
    /// <exception cref="InvalidOperationException">当 Key 为复杂类型且未重写此方法时抛出。</exception>
    public virtual string BuildCacheKey(TKey key)
    {
        if (key is string || key is ValueType)
        {
            return key.ToString() ?? string.Empty;
        }
        throw new InvalidOperationException($"key为自定义DTO，请自行构建缓存key {key}");
    }

    /// <summary>
    /// 获取 Redis 数据库实例。
    /// </summary>
    /// <returns>Redis 数据库实例，如果未启用 Redis 则返回 null。</returns>
    protected abstract IDatabase? GetRedisDatabase();

    /// <summary>
    /// 获取本地缓存（IMemoryCache）实例。
    /// </summary>
    /// <returns>本地缓存实例，如果未启用本地缓存则返回 null。</returns>
    protected abstract IMemoryCache? GetLocalCache();

    /// <summary>
    /// 当本地缓存被设置时的回调方法。
    /// <para>可用于扩展逻辑，例如维护一个辅助的 Key 集合，或更新滑动过期时间。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">缓存值。</param>
    protected virtual void OnLocalCacheSet(TKey key, TValue value) { }

    /// <summary>
    /// 当 Redis 缓存被设置时的回调方法。
    /// <para>可用于扩展逻辑，例如发送 Pub/Sub 通知、记录特定审计日志或更新二级索引。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="expiry">过期时间。</param>
    protected virtual void OnRedisCacheSet(TKey key, TValue value, TimeSpan? expiry) { }

    /// <summary>
    /// 创建本地缓存项配置。
    /// <para>根据 Redis 过期时间计算本地缓存的过期策略。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="redisExpiry">Redis 缓存的过期时间。</param>
    /// <returns>内存缓存项配置选项。</returns>
    protected virtual MemoryCacheEntryOptions CreateLocalCacheEntryOptions(TKey key, TimeSpan? redisExpiry = null)
    {
        var duration = GetLocalCacheExpiry(redisExpiry);
        return new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration };
    }

    /// <summary>
    /// 计算本地缓存的过期时间。
    /// <para>策略：如果 Redis 过期时间小于默认值（5分钟），则使用 Redis 的过期时间；否则使用默认值。
    /// 这样可以减少 L1 和 L2 不一致的时间窗口。</para>
    /// </summary>
    /// <param name="redisExpiry">Redis 缓存的过期时间。</param>
    /// <returns>本地缓存的持续时间。</returns>
    protected virtual TimeSpan GetLocalCacheExpiry(TimeSpan? redisExpiry = null)
    {
        var defaultLocalExpiry = TimeSpan.FromMinutes(5);
        if (redisExpiry.HasValue && redisExpiry.Value < defaultLocalExpiry)
        {
            return redisExpiry.Value;
        }
        return defaultLocalExpiry;
    }

    /// <summary>
    /// 获取完整的 Redis Key（包含缓存名称前缀）。
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>格式为 "{CacheName}:{BuildCacheKey(key)}" 的完整 Key。</returns>
    protected string GetFullKey(TKey key)
    {
        return $"{GetCacheName()}:{BuildCacheKey(key)}";
    }

    /// <summary>
    /// 获取完整的 Redis Key（包含缓存名称前缀）。
    /// </summary>
    /// <param name="cacheKey">已构建的业务 Key 字符串。</param>
    /// <returns>格式为 "{CacheName}:{cacheKey}" 的完整 Key。</returns>
    protected string GetFullKey(string cacheKey)
    {
        return $"{GetCacheName()}:{cacheKey}";
    }

    #endregion

    #region 2. 数据源操作 (Data Source)

    /// <summary>
    /// 回源查询数据。
    /// <para>当 L1 和 L2 缓存都未命中时，会调用此方法从数据库或其他数据源加载数据。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>数据对象。如果数据不存在返回 null。</returns>
    protected virtual Task<TValue?> QueryDataAsync(TKey key)
    {
        // 默认实现返回 null，即不进行回源
        return Task.FromResult<TValue?>(default);
    }

    /// <summary>
    /// 批量查询数据源（可选实现）。
    /// <para>如果支持批量加载，请重写此方法以优化批量获取性能。</para>
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <returns>加载的数据字典。</returns>
    protected virtual Task<Dictionary<TKey, TValue>> QueryDataListAsync(List<TKey> keyList)
    {
        return Task.FromResult(new Dictionary<TKey, TValue>());
    }

    /// <summary>
    /// 更新数据源（可选实现）。
    /// <para>当使用 PutAsync 写入缓存时，如果需要同时回写数据源，请重写此方法。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">值。</param>
    protected virtual Task UpdateDataAsync(TKey key, TValue value)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region 3. 单条缓存操作 (Single Item Operations)

    /// <summary>
    /// 内部获取缓存方法，区分 "未命中" 和 "命中空值"。
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>一个包含缓存状态和值的元组。</returns>
    protected virtual async Task<(CacheStatus Status, TValue? Value)> GetInternalAsync(TKey key)
    {
        var startTime = Stopwatch.GetTimestamp();
        var cacheKey = BuildCacheKey(key);
        var fullKey = GetFullKey(cacheKey);
        var logger = GetLogger();
        var telemetry = GetTelemetryProvider();

        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheGet,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        try
        {
            // 1. Check L1 (Local Cache)
            var localCache = GetLocalCache();
            if (localCache != null && localCache.TryGetValue(fullKey, out object? localObj))
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                logger?.LogCacheHit(GetCacheName(), "L1", cacheKey, elapsed);
                telemetry?.RecordCacheHit(GetCacheName(), CacheLevel.L1, cacheKey, elapsed);

                if (localObj == NullValObj)
                {
                    return (CacheStatus.FoundNull, default);
                }

                return (CacheStatus.Found, (TValue?)localObj);
            }

            if (localCache != null)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                logger?.LogCacheMiss(GetCacheName(), "L1", cacheKey, elapsed);
                telemetry?.RecordCacheMiss(GetCacheName(), CacheLevel.L1, cacheKey, elapsed);
            }

            // 2. Check L2 (Redis)
            var database = GetRedisDatabase();
            if (database != null)
            {
                var value = await database.StringGetAsync(fullKey);

                if (value.HasValue)
                {
                    var elapsed = Stopwatch.GetElapsedTime(startTime);
                    logger?.LogCacheHit(GetCacheName(), "L2", cacheKey, elapsed);
                    telemetry?.RecordCacheHit(GetCacheName(), CacheLevel.L2, cacheKey, elapsed);

                    if (value == NullValString)
                    {
                        // Backfill L1 with NullValObj
                        if (localCache != null)
                        {
                            // 使用较短的过期时间或跟随 Redis
                            var options = CreateLocalCacheEntryOptions(key);
                            localCache.Set(fullKey, NullValObj, options);
                        }
                        return (CacheStatus.FoundNull, default);
                    }

                    var serializer = GetCacheSerializer();
                    var deserializedValue = serializer.DeserializeFromString<TValue>(value!);

                    if (localCache != null && deserializedValue != null)
                    {
                        var options = CreateLocalCacheEntryOptions(key);
                        localCache.Set(fullKey, deserializedValue, options);
                        OnLocalCacheSet(key, deserializedValue);
                    }

                    return (CacheStatus.Found, deserializedValue);
                }
            }

            var elapsedMiss = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheMiss(GetCacheName(), "L2", cacheKey, elapsedMiss);
            telemetry?.RecordCacheMiss(GetCacheName(), CacheLevel.L2, cacheKey, elapsedMiss);
            return (CacheStatus.NotFound, default);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheError(GetCacheName(), "Get", cacheKey, ex, elapsed);
            telemetry?.RecordCacheError(GetCacheName(), "Get", ex, elapsed);
            telemetry?.RecordException(ex);
            return (CacheStatus.NotFound, default);
        }
    }

    /// <summary>
    /// 获取缓存数据（仅查询缓存）。
    /// <para>查询顺序：L1 (Local) -> L2 (Redis)。如果 L1 命中则直接返回；如果 L2 命中则回填 L1 并返回。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>缓存数据，如果未命中则返回 null/default。</returns>
    public virtual async Task<TValue?> GetAsync(TKey key)
    {
        var (_, value) = await GetInternalAsync(key);
        return value;
    }

    /// <summary>
    /// 获取或加载缓存数据（Cache-Aside 模式）。
    /// <para>先查询缓存（GetAsync）；如果未命中，则调用 QueryDataAsync 回源加载，并将结果写入缓存。</para>
    /// <para>已集成内存锁和分布式锁，防止缓存击穿。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>缓存数据或回源加载的数据。</returns>
    public virtual async Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null)
    {
        var startTime = Stopwatch.GetTimestamp();
        var cacheKey = BuildCacheKey(key);
        var logger = GetLogger();
        var telemetry = GetTelemetryProvider();
        var options = GetOptions();

        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheGetOrLoad,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        try
        {
            // 1. First Check (No Lock)
            var (status, cachedValue) = await GetInternalAsync(key);
            if (status == CacheStatus.Found)
            {
                return cachedValue;
            }
            if (status == CacheStatus.FoundNull)
            {
                return default;
            }

            // 2. Memory Lock
            IDisposable? memoryLock = null;
            if (options.Lock.EnabledMemoryLock)
            {
                memoryLock = await _memoryLocker.LockAsync(key, options.Lock.LockTimeout);
            }

            try
            {
                // 3. Double Check (Inside Memory Lock)
                if (options.Lock.EnabledMemoryLock)
                {
                    (status, cachedValue) = await GetInternalAsync(key);
                    if (status == CacheStatus.Found) return cachedValue;
                    if (status == CacheStatus.FoundNull) return default;
                }

                // 4. Distributed Lock (Inside Memory Lock to reduce contention on Redis)
                // 如果启用了分布式锁且 Redis 可用
                var database = GetRedisDatabase();
                var lockKey = $"lock:{GetFullKey(cacheKey)}";
                var lockValue = Guid.NewGuid().ToString();
                var hasDistributedLock = false;

                if (options.Lock.EnabledDistributedLock && database != null)
                {
                    try
                    {
                        // 尝试获取分布式锁
                        // 使用较短的超时时间，因为这只是为了防止击穿，不是为了排队
                        // 如果获取失败，说明有其他节点正在加载，我们可以选择等待或者直接回源（这里选择等待重试）
                        // 为了简化，这里使用简单的自旋等待
                        var lockTimeout = Stopwatch.GetTimestamp();
                        while (true)
                        {
                            if (await database.LockTakeAsync(lockKey, lockValue, options.Lock.DistributedLockExpiry))
                            {
                                hasDistributedLock = true;
                                break;
                            }

                            if (Stopwatch.GetElapsedTime(lockTimeout) > options.Lock.LockTimeout)
                            {
                                logger?.LogWarning($"Failed to acquire distributed lock for key {cacheKey} after {options.Lock.LockTimeout.TotalSeconds}s");
                                break; // 降级为直接回源
                            }

                            await Task.Delay(50); // 简单的等待
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, $"Error acquiring distributed lock for key {cacheKey}");
                        // 降级：继续执行回源
                    }
                }

                try
                {
                    // 5. Triple Check (After acquiring Distributed Lock)
                    // 防止其他节点已经加载完了
                    if (hasDistributedLock)
                    {
                        (status, cachedValue) = await GetInternalAsync(key);
                        if (status == CacheStatus.Found) return cachedValue;
                        if (status == CacheStatus.FoundNull) return default;
                    }

                    // 6. Query Data Source
                    TValue? value = await QueryDataAsync(key);

                    if (value != null)
                    {
                        // 7. Write back to cache
                        // 注意：这里调用 PutAsync 也会尝试获取内存锁（如果 PutAsync 加了锁的话）。
                        // 由于我们已经持有内存锁，如果 _memoryLocker 是不可重入的，这里会死锁。
                        // SemaphoreSlim 是不可重入的！
                        // 解决方案：
                        // A. 使用可重入锁（AsyncLocal 或者是 Thread.CurrentThread ID 检查，但 async/await 比较麻烦）
                        // B. 将 PutAsync 逻辑拆分为 InternalPutAsync（无锁）和 PutAsync（加锁）
                        // C. 在 GetOrLoadAsync 中不调用 PutAsync，而是直接调用 InternalPutAsync

                        // 既然我们要修改 PutAsync 加锁，那么必须拆分。
                        await InternalPutAsync(key, value, expiry);

                        var elapsed = Stopwatch.GetElapsedTime(startTime);
                        logger?.LogDataSourceLoad(GetCacheName(), cacheKey, elapsed, true);
                        telemetry?.RecordDataSourceLoad(GetCacheName(), cacheKey, elapsed, true);
                    }
                    else
                    {
                        // 8. Handle Null Value Caching
                        if (options.CacheNullValues)
                        {
                            // 缓存空值
                            // 传入 default(TValue)，PutAsyncInternal 会检测 value is null 并处理
                            await InternalPutAsync(key, default!, options.NullValueExpiry);
                        }

                        var elapsed = Stopwatch.GetElapsedTime(startTime);
                        logger?.LogDataSourceLoad(GetCacheName(), cacheKey, elapsed, false);
                        telemetry?.RecordDataSourceLoad(GetCacheName(), cacheKey, elapsed, false);
                    }

                    return value;
                }
                finally
                {
                    // Release Distributed Lock
                    if (hasDistributedLock && database != null)
                    {
                        await database.LockReleaseAsync(lockKey, lockValue);
                    }
                }
            }
            finally
            {
                // Release Memory Lock
                memoryLock?.Dispose();
            }
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheError(GetCacheName(), "GetOrLoad", cacheKey, ex, elapsed);
            telemetry?.RecordCacheError(GetCacheName(), "GetOrLoad", ex, elapsed);
            telemetry?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 检查缓存是否存在。
    /// <para>检查顺序：L1 -> L2。只要任意一级存在即返回 true。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>存在返回 true，否则返回 false。</returns>
    public virtual async Task<bool> ExistsAsync(TKey key)
    {
        var cacheKey = BuildCacheKey(key);
        var fullKey = GetFullKey(cacheKey);

        var telemetry = GetTelemetryProvider();
        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheExists,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        var localCache = GetLocalCache();
        if (localCache != null && localCache.TryGetValue(fullKey, out _))
        {
            return true;
        }

        var database = GetRedisDatabase();
        if (database != null)
        {
            return await database.KeyExistsAsync(fullKey);
        }

        return false;
    }

    /// <summary>
    /// 写入缓存。
    /// <para>同时写入 L2（Redis）和 L1（本地缓存）。</para>
    /// <para>已集成内存锁和分布式锁，防止并发写入导致的不一致。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">要缓存的数据。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>写入的数据。</returns>
    public virtual async Task<TValue> PutAsync(TKey key, TValue value, TimeSpan? expiry = null)
    {
        var cacheKey = BuildCacheKey(key);
        var logger = GetLogger();
        var options = GetOptions();

        // 1. Memory Lock
        IDisposable? memoryLock = null;
        if (options.Lock.EnabledMemoryLock)
        {
            try
            {
                memoryLock = await _memoryLocker.LockAsync(key, options.Lock.LockTimeout);
            }
            catch (TimeoutException)
            {
                logger?.LogWarning($"Failed to acquire memory lock for PutAsync key: {cacheKey}");
                // 继续尝试写入，或者抛出异常？为了可用性，我们选择继续写入，但可能会有竞争。
                // 或者直接抛出？视业务需求而定。这里选择记录警告并继续。
            }
        }

        try
        {
            // 2. Distributed Lock
            var database = GetRedisDatabase();
            var lockKey = $"lock:{GetFullKey(cacheKey)}";
            var lockValue = Guid.NewGuid().ToString();
            var hasDistributedLock = false;

            if (options.Lock.EnabledDistributedLock && database != null)
            {
                try
                {
                    // 尝试获取分布式锁，不等待太久
                    if (await database.LockTakeAsync(lockKey, lockValue, options.Lock.DistributedLockExpiry))
                    {
                        hasDistributedLock = true;
                    }
                    else
                    {
                        logger?.LogWarning($"Failed to acquire distributed lock for PutAsync key: {cacheKey}");
                        // 同样，获取失败也继续尝试写入
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, $"Error acquiring distributed lock for PutAsync key {cacheKey}");
                }
            }

            try
            {
                return await InternalPutAsync(key, value, expiry);
            }
            finally
            {
                // Release Distributed Lock
                if (hasDistributedLock && database != null)
                {
                    await database.LockReleaseAsync(lockKey, lockValue);
                }
            }
        }
        finally
        {
            // Release Memory Lock
            memoryLock?.Dispose();
        }
    }

    /// <summary>
    /// 内部写入缓存逻辑（无锁）。
    /// <para>供 GetOrLoadAsync 等内部已持有锁的方法调用。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">要缓存的数据。</param>
    /// <param name="expiry">缓存过期时间。</param>
    /// <returns>写入的数据。</returns>
    protected virtual async Task<TValue> InternalPutAsync(TKey key, TValue value, TimeSpan? expiry = null)
    {
        var startTime = Stopwatch.GetTimestamp();
        var cacheKey = BuildCacheKey(key);
        var fullKey = GetFullKey(cacheKey);
        var logger = GetLogger();
        var telemetry = GetTelemetryProvider();

        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheSet,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        try
        {
            var database = GetRedisDatabase();
            var serializer = GetCacheSerializer();

            string serializedValue;
            // 处理空值
            if (value is null)
            {
                serializedValue = NullValString;
                // 如果未指定过期时间，则使用配置的空值过期时间
                expiry ??= GetOptions().NullValueExpiry;
            }
            else
            {
                serializedValue = serializer.SerializeToString(value);
            }

            if (database != null)
            {
                if (expiry.HasValue)
                {
                    await database.StringSetAsync(fullKey, serializedValue, expiry.Value);
                }
                else
                {
                    await database.StringSetAsync(fullKey, serializedValue);
                }

                OnRedisCacheSet(key, value, expiry);
            }

            var localCache = GetLocalCache();
            if (localCache != null)
            {
                var options = CreateLocalCacheEntryOptions(key, expiry);

                // 处理本地缓存空值
                object localValue = (object?)value ?? NullValObj;
                localCache.Set(fullKey, localValue, options);

                OnLocalCacheSet(key, value!);
            }

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            var dataSize = serializedValue.Length;
            logger?.LogCacheSet(GetCacheName(), "L2", cacheKey, elapsed, expiry, dataSize);
            telemetry?.RecordCacheSet(GetCacheName(), CacheLevel.L2, cacheKey, elapsed, dataSize);

            return value!;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheError(GetCacheName(), "Put", cacheKey, ex, elapsed);
            telemetry?.RecordCacheError(GetCacheName(), "Put", ex, elapsed);
            telemetry?.RecordException(ex);
            // throw; // Resilience: suppress cache write failure
            return value;
        }
    }

    /// <summary>
    /// 仅当缓存不存在时写入（NX 模式）。
    /// <para>通常用于简单的分布式锁或避免并发重复计算。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">要缓存的数据。</param>
    /// <param name="expiry">缓存过期时间。</param>
    /// <returns>如果写入成功（即之前不存在）返回 true，否则返回 false。</returns>
    public virtual async Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null)
    {
        var cacheKey = BuildCacheKey(key);
        var fullKey = GetFullKey(cacheKey);

        var telemetry = GetTelemetryProvider();
        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CachePutIfAbsent,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        var database = GetRedisDatabase();
        if (database != null)
        {
            var serializer = GetCacheSerializer();
            var serializedValue = serializer.SerializeToString(value);
            return await database.StringSetAsync(fullKey, serializedValue, expiry, When.NotExists);
        }
        return false;
    }

    /// <summary>
    /// 更新业务数据并清除缓存。
    /// <para>这是一个组合操作：先调用 <see cref="UpdateDataAsync"/> 更新数据源，然后调用 <see cref="EvictAsync"/> 移除缓存，以保证数据一致性。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">新的业务数据。</param>
    public virtual async Task UpdateAsync(TKey key, TValue value)
    {
        await UpdateDataAsync(key, value);
        await EvictAsync(key);
    }

    /// <summary>
    /// 重新加载缓存。
    /// <para>强制调用 <see cref="QueryDataAsync"/> 从数据源加载最新数据，并更新到缓存中。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="expiry">缓存过期时间。</param>
    /// <returns>重新加载后的最新数据。</returns>
    public virtual async Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null)
    {
        var startTime = Stopwatch.GetTimestamp();
        var cacheKey = BuildCacheKey(key);
        var logger = GetLogger();
        var telemetry = GetTelemetryProvider();

        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheReload,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        try
        {
            var elapsedStart = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheReload(GetCacheName(), cacheKey, elapsedStart);
            var value = await QueryDataAsync(key);

            if (value != null)
            {
                await PutAsync(key, value, expiry);
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                telemetry?.RecordCacheReload(GetCacheName(), cacheKey, elapsed, true);
            }
            else
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                telemetry?.RecordCacheReload(GetCacheName(), cacheKey, elapsed, false);
            }

            return value;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheError(GetCacheName(), "Reload", cacheKey, ex, elapsed);
            telemetry?.RecordCacheError(GetCacheName(), "Reload", ex, elapsed);
            telemetry?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 淘汰（移除）指定 Key 的缓存项。
    /// <para>同时移除本地缓存和 Redis 缓存。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>如果移除成功返回 true，否则返回 false。</returns>
    public virtual async Task<bool> EvictAsync(TKey key)
    {
        var startTime = Stopwatch.GetTimestamp();
        var cacheKey = BuildCacheKey(key);
        var fullKey = GetFullKey(cacheKey);
        var logger = GetLogger();
        var telemetry = GetTelemetryProvider();

        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheEvict,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, cacheKey)
            ]);

        try
        {
            var localCache = GetLocalCache();
            if (localCache != null)
            {
                localCache.Remove(fullKey);
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                logger?.LogCacheEvict(GetCacheName(), "L1", cacheKey, elapsed);
                telemetry?.RecordCacheEvict(GetCacheName(), CacheLevel.L1, cacheKey, elapsed);
            }

            var database = GetRedisDatabase();
            var result = false;
            if (database != null)
            {
                result = await database.KeyDeleteAsync(fullKey);
            }

            var elapsedTotal = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheEvict(GetCacheName(), "L2", cacheKey, elapsedTotal);
            telemetry?.RecordCacheEvict(GetCacheName(), CacheLevel.L2, cacheKey, elapsedTotal);

            return result;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            logger?.LogCacheError(GetCacheName(), "Evict", cacheKey, ex, elapsed);
            telemetry?.RecordCacheError(GetCacheName(), "Evict", ex, elapsed);
            telemetry?.RecordException(ex);
            // throw; // Resilience: suppress cache eviction failure
            return false;
        }
    }

    #endregion

    #region 4. 批量缓存操作 (Batch Operations)

    /// <summary>
    /// 批量获取缓存数据（仅查询缓存）。
    /// <para>优化策略：先查本地缓存；未命中的 Key 聚合后通过 Redis MGET 批量获取。
    /// 这样可以显著减少网络往返次数 (RTT)。</para>
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <returns>包含命中的业务 Key 和对应数据的字典。</returns>
    public virtual async Task<Dictionary<TKey, TValue>> BatchGetAsync(List<TKey> keyList)
    {
        var result = new Dictionary<TKey, TValue>();
        if (keyList.Count == 0) return result;

        var telemetry = GetTelemetryProvider();
        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheBatchGet,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>("key_count", keyList.Count)
            ]);

        var missingKeys = new List<(TKey Key, string FullKey)>();
        var localCache = GetLocalCache();
        var serializer = GetCacheSerializer();

        // 1. 优先从本地缓存获取
        foreach (var key in keyList)
        {
            var fullKey = GetFullKey(key);
            if (localCache != null && localCache.TryGetValue(fullKey, out TValue? val))
            {
                result[key] = val!;
            }
            else
            {
                missingKeys.Add((key, fullKey));
            }
        }

        if (missingKeys.Count == 0) return result;

        // 2. 本地缓存未命中的，使用 MGET 从 Redis 批量获取
        var database = GetRedisDatabase();
        if (database == null) return result;

        try
        {
            var redisKeys = missingKeys.Select(k => (RedisKey)k.FullKey).ToArray();
            var redisValues = await database.StringGetAsync(redisKeys);

            for (int i = 0; i < missingKeys.Count; i++)
            {
                var val = redisValues[i];
                if (!val.HasValue)
                {
                    continue;
                }

                var (key, fullKey) = missingKeys[i];
                var deserialized = serializer.DeserializeFromString<TValue>(val!);
                if (deserialized == null)
                {
                    continue;
                }

                result[key] = deserialized;

                // 回填本地缓存
                if (localCache == null)
                {
                    continue;
                }

                localCache.Set(fullKey, deserialized, CreateLocalCacheEntryOptions(key));
                OnLocalCacheSet(key, deserialized);
            }
        }
        catch (Exception ex)
        {
            GetLogger()?.LogWarning(ex, "BatchGetAsync Redis error");
            telemetry?.RecordException(ex);
        }

        return result;
    }

    /// <summary>
    /// 批量获取或加载缓存。
    /// <para>流程：
    /// 1. 调用 BatchGetAsync 批量查询缓存。
    /// 2. 对于未命中的 Key，调用 QueryDataListAsync 批量回源。
    /// 3. 将回源结果回填到缓存中。</para>
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <param name="expiry">缓存过期时间。</param>
    /// <returns>包含所有请求 Key 和对应数据的字典。</returns>
    public virtual async Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(List<TKey> keyList, TimeSpan? expiry = null)
    {
        if (keyList.Count == 0) return new Dictionary<TKey, TValue>();

        var telemetry = GetTelemetryProvider();
        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheBatchGetOrLoad,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>("key_count", keyList.Count)
            ]);

        // 1. 尝试批量获取缓存
        var result = await BatchGetAsync(keyList);

        // 2. 找出缺失的 Key
        var missingKeys = keyList.Where(k => !result.ContainsKey(k)).ToList();

        if (!missingKeys.Any())
        {
            return result;
        }

        // 3. 批量回源加载
        var loadedData = await QueryDataListAsync(missingKeys);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (loadedData != null)
        {
            foreach (var kvp in loadedData)
            {
                result[kvp.Key] = kvp.Value;

                // 4. 回填缓存
                await BackfillCacheAsync(kvp.Key, kvp.Value, expiry);
            }
        }

        // 5. Handle Null Value Caching
        if (GetOptions().CacheNullValues)
        {
            var nullKeys = missingKeys.Where(k => !result.ContainsKey(k)).ToList();
            foreach (var key in nullKeys)
            {
                await BackfillCacheAsync(key, default!, GetOptions().NullValueExpiry);
            }
        }

        return result;
    }

    /// <summary>
    /// 回填缓存（带锁）。
    /// </summary>
    private async Task BackfillCacheAsync(TKey key, TValue value, TimeSpan? expiry)
    {
        var options = GetOptions();
        var cacheKey = BuildCacheKey(key);

        // 1. Memory Lock
        IDisposable? memoryLock = null;
        if (options.Lock.EnabledMemoryLock)
        {
            try
            {
                memoryLock = await _memoryLocker.LockAsync(key, options.Lock.LockTimeout);
            }
            catch (TimeoutException)
            {
                GetLogger()?.LogWarning($"Failed to acquire memory lock for Backfill key: {cacheKey}");
            }
        }

        try
        {
            // 2. Distributed Lock
            var database = GetRedisDatabase();
            var lockKey = $"lock:{GetFullKey(cacheKey)}";
            var lockValue = Guid.NewGuid().ToString();
            var hasDistributedLock = false;

            if (options.Lock.EnabledDistributedLock && database != null)
            {
                try
                {
                    if (await database.LockTakeAsync(lockKey, lockValue, options.Lock.DistributedLockExpiry))
                    {
                        hasDistributedLock = true;
                    }
                }
                catch (Exception ex)
                {
                    GetLogger()?.LogWarning(ex, $"Error acquiring distributed lock for Backfill key {cacheKey}");
                }
            }

            try
            {
                // 3. Double Check
                // 如果缓存中已经存在值（被并发写入），则放弃回填（认为 DB 数据可能陈旧）
                var exists = await ExistsAsync(key);
                if (!exists)
                {
                    await InternalPutAsync(key, value, expiry);
                }
            }
            finally
            {
                // Release Distributed Lock
                if (hasDistributedLock && database != null)
                {
                    await database.LockReleaseAsync(lockKey, lockValue);
                }
            }
        }
        finally
        {
            // Release Memory Lock
            memoryLock?.Dispose();
        }
    }

    /// <summary>
    /// 批量淘汰（移除）缓存项。
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <returns>成功移除的缓存项数量。</returns>
    public virtual async Task<long> BatchEvictAsync(List<TKey> keyList)
    {
        if (keyList.Count == 0) return 0;

        var telemetry = GetTelemetryProvider();
        using var activity = telemetry?.StartActivity(TelemetryConstants.ActivityNames.CacheBatchEvict,
            tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, GetCacheName()),
                new KeyValuePair<string, object>("key_count", keyList.Count)
            ]);

        // 1. Remove from Local Cache
        var localCache = GetLocalCache();
        if (localCache != null)
        {
            foreach (var key in keyList)
            {
                var fullKey = GetFullKey(key);
                localCache.Remove(fullKey);
            }
        }

        // 2. Remove from Redis
        var database = GetRedisDatabase();
        if (database != null)
        {
            try
            {
                var redisKeys = keyList.Select(k => (RedisKey)GetFullKey(k)).ToArray();
                return await database.KeyDeleteAsync(redisKeys);
            }
            catch (Exception ex)
            {
                GetLogger()?.LogWarning(ex, "BatchEvictAsync Redis error");
                // If Redis fails, we still removed from local cache.
                return 0;
            }
        }

        // If only local cache is used, we assume all provided keys were "evicted" (even if they didn't exist)
        return keyList.Count;
    }

    #endregion
}
