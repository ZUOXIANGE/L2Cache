using L2Cache.Abstractions.Stores;
using L2Cache.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Stores;

/// <summary>
/// 基于 StackExchange.Redis 的 L2 分布式存储实现。
/// <para>
/// 所有操作容忍连接故障：读取失败返回未命中、写入失败返回 false，错误仅记录日志，
/// 由上层保持降级语义（Redis 不可用时退化为纯内存缓存）。
/// </para>
/// </summary>
internal sealed class RedisCacheStore : IL2CacheStore
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly int _database;
    private readonly ILogger _logger;

    public RedisCacheStore(IConnectionMultiplexer multiplexer, L2CacheOptions options, ILogger<RedisCacheStore> logger)
    {
        _multiplexer = multiplexer;
        _database = options.Redis.Database;
        _logger = logger;
    }

    private IDatabase Db => _multiplexer.GetDatabase(_database);

    public async Task<StoreEntry> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await Db.StringGetAsync(key).ConfigureAwait(false);
            return value.HasValue
                ? new StoreEntry(true, (byte[])value!)
                : StoreEntry.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 读取失败，降级为未命中。Key: {Key}", key);
            return StoreEntry.NotFound;
        }
    }

    public async Task<bool> SetAsync(string key, ReadOnlyMemory<byte> payload, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.StringSetAsync(key, payload, ttl, when: onlyIfAbsent ? When.NotExists : When.Always).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 写入失败。Key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 删除失败。Key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.KeyExistsAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 存在性检查失败。Key: {Key}", key);
            return false;
        }
    }

    public async Task<Dictionary<string, StoreEntry>> GetManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, StoreEntry>(keys.Count);
        if (keys.Count == 0)
        {
            return result;
        }

        try
        {
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            var values = await Db.StringGetAsync(redisKeys).ConfigureAwait(false);

            for (var i = 0; i < keys.Count; i++)
            {
                result[keys[i]] = values[i].HasValue
                    ? new StoreEntry(true, (byte[])values[i]!)
                    : StoreEntry.NotFound;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 批量读取失败，降级为全部未命中。KeyCount: {Count}", keys.Count);
        }

        return result;
    }

    public async Task<HashSet<string>> SetManyAsync(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> items, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default)
    {
        var successKeys = new HashSet<string>();
        if (items.Count == 0)
        {
            return successKeys;
        }

        try
        {
            var batch = Db.CreateBatch();
            var tasks = new List<(string Key, Task<bool> Task)>(items.Count);

            foreach (var (key, payload) in items)
            {
                var task = batch.StringSetAsync(key, payload, ttl, when: onlyIfAbsent ? When.NotExists : When.Always);
                tasks.Add((key, task));
            }

            batch.Execute();
            await Task.WhenAll(tasks.Select(t => t.Task)).ConfigureAwait(false);

            foreach (var (key, task) in tasks)
            {
                if (task.Result)
                {
                    successKeys.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 批量写入失败。KeyCount: {Count}", items.Count);
        }

        return successKeys;
    }

    public async Task<long> RemoveManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        try
        {
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            return await Db.KeyDeleteAsync(redisKeys).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 批量删除失败。KeyCount: {Count}", keys.Count);
            return 0;
        }
    }

    public async Task<bool> AcquireLockAsync(string lockKey, string token, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.LockTakeAsync(lockKey, token, expiry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "分布式锁获取失败。LockKey: {LockKey}", lockKey);
            return false;
        }
    }

    public async Task<bool> ReleaseLockAsync(string lockKey, string token, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.LockReleaseAsync(lockKey, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "分布式锁释放失败。LockKey: {LockKey}", lockKey);
            return false;
        }
    }
}
