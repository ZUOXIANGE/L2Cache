using L2Cache.Examples.Models;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace L2Cache.Examples.Services;

/// <summary>
/// 演示直接继承 AbstractCacheService 的自定义缓存服务。
/// <para>这种方式允许你完全控制依赖注入和底层实现，而不是使用默认的 L2CacheService。</para>
/// </summary>
public class CustomUserCacheService : AbstractCacheService<int, UserDto>
{
    private readonly IMemoryCache _localCache;
    private readonly IDatabase _redisDb;
    private readonly ILogger<CustomUserCacheService> _logger;

    // 我们可以注入任何需要的依赖，比如 DbContext
    // private readonly MyDbContext _dbContext;

    public CustomUserCacheService(
        IMemoryCache localCache,
        IDatabase redisDb,
        ILogger<CustomUserCacheService> logger)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _redisDb = redisDb ?? throw new ArgumentNullException(nameof(redisDb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string GetCacheName() => "users";

    protected override IMemoryCache? GetLocalCache() => _localCache;

    protected override IDatabase? GetRedisDatabase() => _redisDb;

    protected override ILogger? GetLogger() => _logger;

    public override string BuildCacheKey(int key) => $"user:{key}";

    /// <summary>
    /// 实现回源逻辑
    /// </summary>
    protected override async Task<UserDto?> QueryDataAsync(int key)
    {
        _logger.LogInformation("Fetching user {Key} from simulated database...", key);
        
        // 模拟数据库查询延迟
        await Task.Delay(50);

        if (key <= 0) return null;

        return new UserDto
        {
            Id = key,
            Username = $"User_{key}",
            Email = $"user{key}@example.com",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 重写 Redis 写入回调
    /// </summary>
    protected override void OnRedisCacheSet(int key, UserDto value, TimeSpan? expiry)
    {
        _logger.LogInformation("Custom Hook: User {Id} cached in Redis.", key);
    }
}
