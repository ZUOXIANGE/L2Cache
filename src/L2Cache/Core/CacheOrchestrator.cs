using System.Diagnostics;
using L2Cache.Abstractions.Invalidation;
using L2Cache.Abstractions.Policies;
using L2Cache.Abstractions.Stores;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Logging;
using Microsoft.Extensions.Logging;

namespace L2Cache.Core;

/// <summary>
/// 多级缓存核心编排器（进程单例）。
/// <para>
/// 承载全部缓存管道逻辑：读管道（L1→L2）、Cache-Aside 管道（读→锁→回源→回填）、
/// 写管道（锁→L2→L1→失效广播）、批量管道（MGET / Pipeline / 回源合并）。
/// 泛型仅出现在方法签名上，描述符 <see cref="CacheDescriptor{TKey,TValue}"/> 携带区域策略。
/// </para>
/// </summary>
public sealed class CacheOrchestrator
{
    private readonly IL1CacheStore? _l1;
    private readonly IL2CacheStore? _l2;
    private readonly ICacheInvalidationBus? _invalidationBus;
    private readonly ITelemetryProvider _telemetry;
    private readonly ILogger _logger;
    private long _version = Environment.TickCount64;

    public CacheOrchestrator(
        IL1CacheStore? l1 = null,
        IL2CacheStore? l2 = null,
        ICacheInvalidationBus? invalidationBus = null,
        ITelemetryProvider? telemetry = null,
        ILogger? logger = null)
    {
        _l1 = l1;
        _l2 = l2;
        _invalidationBus = invalidationBus;
        _telemetry = telemetry ?? new Telemetry.NoOpTelemetryProvider();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    #region 读管道

    /// <summary>查询缓存（不回源）。读取顺序：L1 → L2（L2 命中后回填 L1）。</summary>
    public async Task<CacheValue<TValue>> GetAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheGet, descriptor.CacheName, cacheKey);

        try
        {
            return await ReadAsync(descriptor, key, fullKey, activity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError(descriptor.CacheName, "Get", cacheKey, ex, Stopwatch.GetElapsedTime(start));
            throw;
        }
    }

    /// <summary>
    /// Cache-Aside 管道：无锁首查 → 加锁 → 双查 → 回源 → 回填（含空值缓存）。
    /// 锁获取失败时降级为直接回源（可用性优先）。
    /// </summary>
    public async Task<CacheValue<TValue>> GetOrLoadAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, ILoader<TKey, TValue> loader, TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        if (loader is null)
        {
            throw new InvalidOperationException(
                $"缓存区域 '{descriptor.CacheName}' 未配置回源加载器（AddCache(...).WithLoader），无法执行 GetOrLoadAsync。");
        }

        var start = Stopwatch.GetTimestamp();
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheGetOrLoad, descriptor.CacheName, cacheKey);

        try
        {
            // 1. 无锁首查
            var result = await ReadAsync(descriptor, key, fullKey, activity, cancellationToken).ConfigureAwait(false);
            if (result.Status != CacheStatus.NotFound)
            {
                return result;
            }

            // 2. 加锁（内存锁拦截本机并发，分布式锁拦截跨节点并发；null = 未配置锁）
            var lockHandle = descriptor.Lock is null ? null : await descriptor.Lock.AcquireAsync(fullKey, cancellationToken).ConfigureAwait(false);

            try
            {
                // 3. 双查（仅在成功获取锁后有意义；降级句柄跳过）
                if (lockHandle is { Acquired: true })
                {
                    result = await ReadAsync(descriptor, key, fullKey, activity, cancellationToken).ConfigureAwait(false);
                    if (result.Status != CacheStatus.NotFound)
                    {
                        return result;
                    }
                }

                // 4. 回源
                var loaded = await loader.LoadAsync(key, cancellationToken).ConfigureAwait(false);
                activity?.SetTag(TelemetryConstants.TagNames.Source, TelemetryConstants.TagValues.DataSource);
                var elapsed = Stopwatch.GetElapsedTime(start);

                if (loaded != null)
                {
                    await WriteAsync(descriptor, key, cacheKey, fullKey, loaded, expiry, cancellationToken).ConfigureAwait(false);
                    _telemetry.RecordDataSourceLoad(descriptor.CacheName, cacheKey, elapsed, success: true);
                    _logger.LogDataSourceLoad(descriptor.CacheName, cacheKey, elapsed, success: true);
                    return CacheValue.Found(loaded);
                }

                // 5. 空值缓存（防穿透）
                if (descriptor.NullValue.Enabled)
                {
                    await WriteNullAsync(descriptor, cacheKey, fullKey, cancellationToken).ConfigureAwait(false);
                }

                _telemetry.RecordDataSourceLoad(descriptor.CacheName, cacheKey, elapsed, success: false);
                _logger.LogDataSourceLoad(descriptor.CacheName, cacheKey, elapsed, success: false);
                return CacheValue.NotFound<TValue>();
            }
            finally
            {
                if (lockHandle != null)
                {
                    await lockHandle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            LogError(descriptor.CacheName, "GetOrLoad", cacheKey, ex, Stopwatch.GetElapsedTime(start));
            throw;
        }
    }

    /// <summary>内部读取管道：L1 → L2（L2 命中回填 L1）。返回 Found / FoundNull / NotFound。</summary>
    private async Task<CacheValue<TValue>> ReadAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, string fullKey, Activity? activity, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        var cacheName = descriptor.CacheName;

        // 1. L1（对象缓存，命中免反序列化）
        if (_l1 != null)
        {
            var l1Entry = _l1.GetValue(fullKey);
            if (l1Entry.Found)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogCacheHit(cacheName, "L1", fullKey, Stopwatch.GetElapsedTime(start));
                }

                _telemetry.RecordCacheHit(cacheName, CacheLevel.L1, fullKey, Stopwatch.GetElapsedTime(start));
                activity?.SetTag(TelemetryConstants.TagNames.Level, "L1");

                return l1Entry.IsNullValue
                    ? CacheValue.FoundNull<TValue>()
                    : CacheValue.Found((TValue)l1Entry.Value!);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogCacheMiss(cacheName, "L1", fullKey, Stopwatch.GetElapsedTime(start));
            }

            _telemetry.RecordCacheMiss(cacheName, CacheLevel.L1, fullKey, Stopwatch.GetElapsedTime(start));
        }

        // 2. L2（字节缓存）
        if (_l2 != null)
        {
            var entry = await _l2.GetAsync(fullKey, cancellationToken).ConfigureAwait(false);
            if (entry.Found)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogCacheHit(cacheName, "L2", fullKey, Stopwatch.GetElapsedTime(start));
                }

                _telemetry.RecordCacheHit(cacheName, CacheLevel.L2, fullKey, Stopwatch.GetElapsedTime(start));
                activity?.SetTag(TelemetryConstants.TagNames.Level, "L2");

                if (descriptor.NullValue.IsNullPayload(entry.Payload))
                {
                    _l1?.SetValue(fullKey, null, descriptor.Expiry.ResolveL1Ttl(descriptor.NullValue.Ttl));
                    return CacheValue.FoundNull<TValue>();
                }

                var value = descriptor.Serializer.Deserialize<TValue>(entry.Payload);
                if (value != null)
                {
                    _l1?.SetValue(fullKey, value, descriptor.Expiry.ResolveL1Ttl(null));
                    descriptor.TrackKey(key);
                    return CacheValue.Found(value);
                }
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogCacheMiss(cacheName, "L2", fullKey, Stopwatch.GetElapsedTime(start));
                }

                _telemetry.RecordCacheMiss(cacheName, CacheLevel.L2, fullKey, Stopwatch.GetElapsedTime(start));
            }
        }

        return CacheValue.NotFound<TValue>();
    }

    #endregion

    #region 写管道

    /// <summary>显式写入缓存（覆盖写）。加锁后执行写管道，防止并发写冲突。</summary>
    public async Task PutAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheSet, descriptor.CacheName, cacheKey);

        var lockHandle = descriptor.Lock is null ? null : await descriptor.Lock.AcquireAsync(fullKey, cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(descriptor, key, cacheKey, fullKey, value, expiry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lockHandle != null)
            {
                await lockHandle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>仅当 L2 不存在时写入（NX 模式）。写入成功后回填 L1 并广播失效。</summary>
    public async Task<bool> PutIfAbsentAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CachePutIfAbsent, descriptor.CacheName, cacheKey);

        if (_l2 == null)
        {
            return false;
        }

        var isNull = value is null;
        var payload = isNull ? descriptor.NullValue.NullPayload : descriptor.Serializer.Serialize(value);
        var ttl = descriptor.Expiry.ResolveL2Ttl(expiry, isNull);

        var success = await _l2.SetAsync(fullKey, payload, ttl, onlyIfAbsent: true, cancellationToken).ConfigureAwait(false);
        _telemetry.RecordCacheSet(descriptor.CacheName, CacheLevel.L2, cacheKey, Stopwatch.GetElapsedTime(start), payload.Length);

        if (success)
        {
            _l1?.SetValue(fullKey, value, descriptor.Expiry.ResolveL1Ttl(ttl));
            if (value != null)
            {
                descriptor.TrackKey(key);
            }

            await PublishInvalidationAsync(descriptor, cacheKey, cancellationToken).ConfigureAwait(false);
        }

        return success;
    }

    /// <summary>内部写管道（无锁）：L2 写入 → L1 回填 → 失效广播。空值走空值 TTL。</summary>
    private async Task WriteAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, string cacheKey, string fullKey, TValue value, TimeSpan? expiry, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        var isNull = value is null;
        var payload = isNull ? descriptor.NullValue.NullPayload : descriptor.Serializer.Serialize(value);
        var ttl = descriptor.Expiry.ResolveL2Ttl(expiry, isNull);

        var l2Succeeded = false;
        if (_l2 != null)
        {
            l2Succeeded = await _l2.SetAsync(fullKey, payload, ttl, onlyIfAbsent: false, cancellationToken).ConfigureAwait(false);
            _telemetry.RecordCacheSet(descriptor.CacheName, CacheLevel.L2, cacheKey, Stopwatch.GetElapsedTime(start), payload.Length);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogCacheSet(descriptor.CacheName, "L2", cacheKey, Stopwatch.GetElapsedTime(start), ttl, payload.Length);
            }
        }

        _l1?.SetValue(fullKey, value, descriptor.Expiry.ResolveL1Ttl(ttl));
        if (!isNull)
        {
            descriptor.TrackKey(key);
        }

        if (l2Succeeded)
        {
            await PublishInvalidationAsync(descriptor, cacheKey, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>写入空值缓存项（防穿透）。</summary>
    private async Task WriteNullAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, string cacheKey, string fullKey, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var ttl = descriptor.Expiry.ResolveL2Ttl(null, isNullValue: true);

        var l2Succeeded = false;
        if (_l2 != null)
        {
            l2Succeeded = await _l2.SetAsync(fullKey, descriptor.NullValue.NullPayload, ttl, onlyIfAbsent: false, cancellationToken).ConfigureAwait(false);
        }

        _l1?.SetValue(fullKey, null, descriptor.Expiry.ResolveL1Ttl(ttl));

        if (l2Succeeded)
        {
            await PublishInvalidationAsync(descriptor, cacheKey, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region 淘汰与重载

    /// <summary>移除指定 Key 的 L1 + L2 缓存，并广播失效消息。</summary>
    public async Task<bool> EvictAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheEvict, descriptor.CacheName, cacheKey);

        _l1?.Remove(fullKey);
        descriptor.UntrackKey(key);
        _telemetry.RecordCacheEvict(descriptor.CacheName, CacheLevel.L1, cacheKey, Stopwatch.GetElapsedTime(start));
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogCacheEvict(descriptor.CacheName, "L1", cacheKey, Stopwatch.GetElapsedTime(start));
        }

        var removed = false;
        if (_l2 != null)
        {
            removed = await _l2.RemoveAsync(fullKey, cancellationToken).ConfigureAwait(false);
            _telemetry.RecordCacheEvict(descriptor.CacheName, CacheLevel.L2, cacheKey, Stopwatch.GetElapsedTime(start));
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogCacheEvict(descriptor.CacheName, "L2", cacheKey, Stopwatch.GetElapsedTime(start));
            }

            await PublishInvalidationAsync(descriptor, cacheKey, cancellationToken).ConfigureAwait(false);
        }

        return _l2 != null ? removed : _l1 != null;
    }

    /// <summary>强制回源加载最新数据并写回缓存。需要提供 Loader。</summary>
    public async Task<TValue?> ReloadAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, ILoader<TKey, TValue> loader, TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        if (loader is null)
        {
            throw new InvalidOperationException(
                $"缓存区域 '{descriptor.CacheName}' 未配置回源加载器（AddCache(...).WithLoader），无法执行 ReloadAsync。");
        }

        var start = Stopwatch.GetTimestamp();
        var cacheKey = descriptor.KeyBuilder.Build(key);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheReload, descriptor.CacheName, cacheKey);

        var value = await loader.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(TelemetryConstants.TagNames.Source, TelemetryConstants.TagValues.DataSource);

        if (value != null)
        {
            var fullKey = descriptor.BuildFullKey(cacheKey);
            await WriteAsync(descriptor, key, cacheKey, fullKey, value, expiry, cancellationToken).ConfigureAwait(false);
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        _logger.LogCacheReload(descriptor.CacheName, cacheKey, elapsed, expiry);
        _telemetry.RecordDataSourceLoad(descriptor.CacheName, cacheKey, elapsed, value != null);

        return value;
    }

    /// <summary>检查缓存是否存在（L1 或 L2 任一存在即为 true）。</summary>
    public async Task<bool> ExistsAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var cacheKey = descriptor.KeyBuilder.Build(key);
        var fullKey = descriptor.BuildFullKey(cacheKey);

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheExists, descriptor.CacheName, cacheKey);

        if (_l1 != null && _l1.Exists(fullKey))
        {
            _telemetry.RecordCacheExists(descriptor.CacheName, cacheKey, exists: true);
            return true;
        }

        var exists = _l2 != null && await _l2.ExistsAsync(fullKey, cancellationToken).ConfigureAwait(false);
        _telemetry.RecordCacheExists(descriptor.CacheName, cacheKey, exists);
        return exists;
    }

    /// <summary>检查 Key 是否仍存在于 L1（供后台刷新判断是否继续跟踪）。</summary>
    public bool ExistsInL1<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key)
        where TKey : notnull
    {
        return _l1 != null && _l1.Exists(descriptor.BuildFullKey(key));
    }

    /// <summary>仅从 L2 读取最新值（不回填 L1、不回源）。供后台刷新使用。</summary>
    public async Task<TValue?> GetL2ValueAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, TKey key, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        if (_l2 == null)
        {
            return default;
        }

        var entry = await _l2.GetAsync(descriptor.BuildFullKey(key), cancellationToken).ConfigureAwait(false);
        if (!entry.Found || descriptor.NullValue.IsNullPayload(entry.Payload))
        {
            return default;
        }

        return descriptor.Serializer.Deserialize<TValue>(entry.Payload);
    }

    #endregion

    #region 批量管道

    /// <summary>批量查询缓存（不回源）。结果仅包含命中的非空值。</summary>
    public async Task<Dictionary<TKey, TValue>> BatchGetAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheBatchGet, descriptor.CacheName, keys.Count);

        var (found, _) = await BatchReadAsync(descriptor, keys, cancellationToken).ConfigureAwait(false);

        _telemetry.RecordBatchOperation(descriptor.CacheName, "batch_get", keys.Count, Stopwatch.GetElapsedTime(start), found.Count);
        return found;
    }

    /// <summary>
    /// 批量获取或加载：批量读 → 缺失 Key 批量回源 → Pipeline 回填（NX，避免覆盖并发写入）→ 空值缓存。
    /// 已命中"空值"的 Key 不会重复回源。
    /// </summary>
    public async Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, ILoader<TKey, TValue> loader, IReadOnlyList<TKey> keys, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        if (loader is null)
        {
            throw new InvalidOperationException(
                $"缓存区域 '{descriptor.CacheName}' 未配置回源加载器（AddCache(...).WithLoader），无法执行 BatchGetOrLoadAsync。");
        }

        var start = Stopwatch.GetTimestamp();

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheBatchGetOrLoad, descriptor.CacheName, keys.Count);

        // 1. 批量读（区分"未命中"与"命中空值"，空值不回源）
        var (found, nullHits) = await BatchReadAsync(descriptor, keys, cancellationToken).ConfigureAwait(false);
        var missing = new List<TKey>(keys.Count);
        var missingSeen = new HashSet<TKey>();
        foreach (var k in keys)
        {
            if (!found.ContainsKey(k) && !nullHits.Contains(k) && missingSeen.Add(k))
            {
                missing.Add(k);
            }
        }

        if (missing.Count > 0)
        {
            // 2. 批量回源
            var loaded = await loader.LoadManyAsync(missing, cancellationToken).ConfigureAwait(false);
            foreach (var kvp in loaded)
            {
                found[kvp.Key] = kvp.Value;
            }

            // 3. Pipeline 回填（NX 模式：仅当 Key 仍不存在时写入，L1 只回填写入成功的 Key）
            await BatchBackfillAsync(descriptor, loaded, expiry, cancellationToken).ConfigureAwait(false);

            // 4. 空值缓存（防穿透）
            if (descriptor.NullValue.Enabled)
            {
                var nullKeys = missing.Where(k => !loaded.ContainsKey(k)).ToList();
                if (nullKeys.Count > 0)
                {
                    var nullData = new Dictionary<TKey, TValue>();
                    foreach (var k in nullKeys)
                    {
                        nullData[k] = default!;
                    }

                    await BatchBackfillAsync(descriptor, nullData, null, cancellationToken, isNull: true).ConfigureAwait(false);
                }
            }
        }

        _telemetry.RecordBatchOperation(descriptor.CacheName, "batch_get_or_load", keys.Count, Stopwatch.GetElapsedTime(start), found.Count);
        return found;
    }

    /// <summary>批量读管道：L1 逐 Key 查询 → 缺失 Key 走 L2 MGET 并回填 L1。返回命中值与空值命中集合。</summary>
    private async Task<(Dictionary<TKey, TValue> Found, HashSet<TKey> NullHits)> BatchReadAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, IReadOnlyList<TKey> keys, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var found = new Dictionary<TKey, TValue>(keys.Count);
        var nullHits = new HashSet<TKey>();
        if (keys.Count == 0)
        {
            return (found, nullHits);
        }

        var missing = new List<(TKey Key, string FullKey)>(keys.Count);

        // 1. L1 逐 Key 查询（对象缓存，命中免反序列化）
        foreach (var key in keys)
        {
            var fullKey = descriptor.BuildFullKey(key);
            if (_l1 != null)
            {
                var l1Entry = _l1.GetValue(fullKey);
                if (l1Entry.Found)
                {
                    if (l1Entry.IsNullValue)
                    {
                        nullHits.Add(key);
                    }
                    else
                    {
                        found[key] = (TValue)l1Entry.Value!;
                    }

                    continue;
                }
            }

            missing.Add((key, fullKey));
        }

        // 2. L2 MGET + 回填 L1
        if (missing.Count > 0 && _l2 != null)
        {
            try
            {
                var fullKeys = new List<string>(missing.Count);
                foreach (var m in missing)
                {
                    fullKeys.Add(m.FullKey);
                }

                var entries = await _l2.GetManyAsync(fullKeys, cancellationToken).ConfigureAwait(false);

                foreach (var (key, fullKey) in missing)
                {
                    if (!entries.TryGetValue(fullKey, out var entry) || !entry.Found)
                    {
                        continue;
                    }

                    if (descriptor.NullValue.IsNullPayload(entry.Payload))
                    {
                        nullHits.Add(key);
                        _l1?.SetValue(fullKey, null, descriptor.Expiry.ResolveL1Ttl(descriptor.NullValue.Ttl));
                        continue;
                    }

                    var value = descriptor.Serializer.Deserialize<TValue>(entry.Payload);
                    if (value != null)
                    {
                        found[key] = value;
                        _l1?.SetValue(fullKey, value, descriptor.Expiry.ResolveL1Ttl(null));
                        descriptor.TrackKey(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量读取 L2 失败，降级为仅 L1 结果。CacheName: {CacheName}", descriptor.CacheName);
                _telemetry.RecordException(ex);
            }
        }

        return (found, nullHits);
    }

    /// <summary>批量回填：L2 Pipeline 写入（NX），L1 仅回填写入成功的 Key。</summary>
    private async Task BatchBackfillAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, Dictionary<TKey, TValue> data, TimeSpan? expiry, CancellationToken cancellationToken, bool isNull = false)
        where TKey : notnull
    {
        if (data.Count == 0)
        {
            return;
        }

        var payloadItems = new Dictionary<string, ReadOnlyMemory<byte>>(data.Count);
        var keyMap = new Dictionary<string, TKey>(data.Count);
        foreach (var (key, value) in data)
        {
            var fullKey = descriptor.BuildFullKey(key);
            payloadItems[fullKey] = isNull || value is null
                ? descriptor.NullValue.NullPayload
                : descriptor.Serializer.Serialize(value);
            keyMap[fullKey] = key;
        }

        var successKeys = payloadItems.Keys.ToList();
        if (_l2 != null)
        {
            var ttl = descriptor.Expiry.ResolveL2Ttl(expiry, isNull);
            successKeys = [.. (await _l2.SetManyAsync(payloadItems, ttl, onlyIfAbsent: true, cancellationToken).ConfigureAwait(false))];
        }

        foreach (var fullKey in successKeys)
        {
            var key = keyMap[fullKey];
            var value = data[key];
            _l1?.SetValue(fullKey, value, descriptor.Expiry.ResolveL1Ttl(isNull ? descriptor.NullValue.Ttl : expiry));

            if (!isNull && value != null)
            {
                descriptor.TrackKey(key);
            }
        }
    }

    /// <summary>批量写入缓存（Pipeline 优化，覆盖写）。</summary>
    public async Task BatchPutAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, IReadOnlyDictionary<TKey, TValue> data, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        if (data.Count == 0)
        {
            return;
        }

        // 1. 按空值/非空值分组（两者的 TTL 不同），每 Key 仅构建一次 CacheKey/FullKey
        var normalItems = new Dictionary<string, (TKey Key, TValue Value, string CacheKey)>();
        var nullKeys = new List<(TKey Key, string FullKey, string CacheKey)>();
        foreach (var (key, value) in data)
        {
            var cacheKey = descriptor.KeyBuilder.Build(key);
            var fullKey = descriptor.BuildFullKey(cacheKey);

            if (value is null)
            {
                nullKeys.Add((key, fullKey, cacheKey));
            }
            else
            {
                normalItems[fullKey] = (key, value, cacheKey);
            }
        }

        // 2. L2 Pipeline 写入（覆盖写）
        var successKeys = new HashSet<string>();
        if (_l2 != null)
        {
            if (normalItems.Count > 0)
            {
                var items = new Dictionary<string, ReadOnlyMemory<byte>>(normalItems.Count);
                foreach (var (fullKey, item) in normalItems)
                {
                    items[fullKey] = descriptor.Serializer.Serialize(item.Value);
                }

                successKeys.UnionWith(await _l2.SetManyAsync(items, descriptor.Expiry.ResolveL2Ttl(expiry), onlyIfAbsent: false, cancellationToken).ConfigureAwait(false));
            }

            if (nullKeys.Count > 0)
            {
                var nullItems = new Dictionary<string, ReadOnlyMemory<byte>>(nullKeys.Count);
                foreach (var (_, fullKey, _) in nullKeys)
                {
                    nullItems[fullKey] = descriptor.NullValue.NullPayload;
                }

                successKeys.UnionWith(await _l2.SetManyAsync(nullItems, descriptor.Expiry.ResolveL2Ttl(expiry, isNullValue: true), onlyIfAbsent: false, cancellationToken).ConfigureAwait(false));
            }
        }

        // 3. L1 回填（仅回填 L2 写入成功的 Key；未启用 L2 时全部回填）
        if (_l1 != null)
        {
            foreach (var (fullKey, item) in normalItems)
            {
                if (_l2 == null || successKeys.Contains(fullKey))
                {
                    _l1.SetValue(fullKey, item.Value, descriptor.Expiry.ResolveL1Ttl(descriptor.Expiry.ResolveL2Ttl(expiry)));
                    descriptor.TrackKey(item.Key);
                }
            }

            foreach (var (key, fullKey, _) in nullKeys)
            {
                if (_l2 == null || successKeys.Contains(fullKey))
                {
                    _l1.SetValue(fullKey, null, descriptor.Expiry.ResolveL1Ttl(descriptor.Expiry.ResolveL2Ttl(expiry, isNullValue: true)));
                }
            }
        }

        // 4. 失效广播
        if (_l2 != null && successKeys.Count > 0)
        {
            foreach (var (fullKey, item) in normalItems)
            {
                if (successKeys.Contains(fullKey))
                {
                    await PublishInvalidationAsync(descriptor, item.CacheKey, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var (_, fullKey, cacheKey) in nullKeys)
            {
                if (successKeys.Contains(fullKey))
                {
                    await PublishInvalidationAsync(descriptor, cacheKey, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _telemetry.RecordBatchOperation(descriptor.CacheName, "batch_put", data.Count, Stopwatch.GetElapsedTime(start), successKeys.Count);
    }

    /// <summary>批量移除缓存（L1 + L2），并广播失效消息。</summary>
    public async Task<long> BatchEvictAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var start = Stopwatch.GetTimestamp();
        if (keys.Count == 0)
        {
            return 0;
        }

        using var activity = StartActivity(TelemetryConstants.ActivityNames.CacheBatchEvict, descriptor.CacheName, keys.Count);

        // 1. L1 移除
        if (_l1 != null)
        {
            foreach (var key in keys)
            {
                _l1.Remove(descriptor.BuildFullKey(key));
                descriptor.UntrackKey(key);
            }
        }

        // 2. L2 批量移除
        long removed = 0;
        if (_l2 != null)
        {
            var fullKeys = keys.Select(k => descriptor.BuildFullKey(k)).ToList();
            removed = await _l2.RemoveManyAsync(fullKeys, cancellationToken).ConfigureAwait(false);

            // 3. 失效广播
            foreach (var key in keys)
            {
                await PublishInvalidationAsync(descriptor, descriptor.KeyBuilder.Build(key), cancellationToken).ConfigureAwait(false);
            }
        }

        _telemetry.RecordBatchOperation(descriptor.CacheName, "batch_evict", keys.Count, Stopwatch.GetElapsedTime(start), (int)removed);
        return _l2 != null ? removed : keys.Count;
    }

    #endregion

    #region 失效广播

    private async Task PublishInvalidationAsync<TKey, TValue>(CacheDescriptor<TKey, TValue> descriptor, string cacheKey, CancellationToken cancellationToken)
        where TKey : notnull
    {
        if (!descriptor.Options.PublishInvalidation || _invalidationBus == null)
        {
            return;
        }

        await _invalidationBus.PublishAsync(
            new InvalidationMessage(descriptor.CacheName, cacheKey, NextVersion()),
            cancellationToken).ConfigureAwait(false);
    }

    private long NextVersion() => Interlocked.Increment(ref _version);

    #endregion

    private void LogError(string cacheName, string operation, string key, Exception exception, TimeSpan elapsed)
    {
        _logger.LogCacheError(cacheName, operation, key, exception, elapsed);
        _telemetry.RecordCacheError(cacheName, operation, exception, elapsed);
        _telemetry.RecordException(exception);
    }

    /// <summary>
    /// 启动遥测 Activity；遥测未启用时直接返回 null，避免 tags 数组与装箱的无效分配。
    /// </summary>
    private Activity? StartActivity(string name, string cacheName, string keyPattern)
        => _telemetry.IsEnabled
            ? _telemetry.StartActivity(name, tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, cacheName),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyPattern, keyPattern)
            ])
            : null;

    /// <inheritdoc cref="StartActivity(string, string, string)" />
    private Activity? StartActivity(string name, string cacheName, int keyCount)
        => _telemetry.IsEnabled
            ? _telemetry.StartActivity(name, tags:
            [
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.CacheName, cacheName),
                new KeyValuePair<string, object>(TelemetryConstants.TagNames.KeyCount, keyCount)
            ])
            : null;
}
