using L2Cache.Extensions;
using L2Cache.Background;
using L2Cache.Abstractions;
using L2Cache.Serializers.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using L2Cache.Tests.Functional.Fixtures;
using StackExchange.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 后台刷新功能测试
/// 测试缓存的后台刷新机制，包括不同的刷新策略和Redis数据变更后的本地缓存更新
/// </summary>
[Collection("Shared Test Collection")]
public class BackgroundRefreshTests
{
    private readonly RedisTestFixture _redisFixture;

    public BackgroundRefreshTests(RedisTestFixture redisFixture)
    {
        _redisFixture = redisFixture;
    }

    /// <summary>
    /// 测试用的刷新策略
    /// 根据Key的前缀返回不同的刷新间隔
    /// </summary>
    private class TestRefreshPolicy : ICacheRefreshPolicy<string, string>
    {
        public TimeSpan? GetRefreshInterval(string key)
        {
            if (key.StartsWith("fast"))
            {
                return TimeSpan.FromMilliseconds(200);
            }
            if (key.StartsWith("slow"))
            {
                return TimeSpan.FromSeconds(5);
            }
            return null; // 默认值
        }
    }

    /// <summary>
    /// 测试配置了刷新策略时，后台刷新应使用不同的间隔
    /// </summary>
    [Fact]
    public async Task BackgroundRefresh_ShouldUseDifferentIntervals_WhenPolicyConfigured()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _redisFixture.ConnectionString;
            options.BackgroundRefresh.Enabled = true;
            options.BackgroundRefresh.Interval = TimeSpan.FromSeconds(10); // 全局默认慢速
        });

        // 注册自定义策略
        services.AddL2CacheRefresh<string, string>(sp => new TestRefreshPolicy());

        var provider = services.BuildServiceProvider();
        
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheRefreshBackgroundService<string, string>>()
            .First();
        
        await hostedService.StartAsync(CancellationToken.None);

        var cacheService = provider.GetRequiredService<ICacheService<string, string>>();
        var serializer = new JsonCacheSerializer();

        // Act (执行)
        // 1. 写入快速刷新和慢速刷新的Key
        var fastKey = "fast_key";
        var slowKey = "slow_key";
        await cacheService.PutAsync(fastKey, "v1");
        await cacheService.PutAsync(slowKey, "v1");

        // 2. 直接更新Redis (模拟外部数据源更新)
        var redis = ConnectionMultiplexer.Connect(_redisFixture.ConnectionString);
        var db = redis.GetDatabase();
        // 缓存名称为 "String" (基于 TValue 类型名称)
        // L2CacheService 中的Key格式为 $"{GetCacheName()}:{BuildCacheKey(key)}"
        
        await db.StringSetAsync($"String:{fastKey}", serializer.SerializeToString("v2"));
        await db.StringSetAsync($"String:{slowKey}", serializer.SerializeToString("v2"));

        // 3. 等待快速刷新的间隔 (200ms) + 缓冲时间
        await Task.Delay(1000);

        // Verify Fast Key Refreshed (L1 应该已从 Redis 更新)
        var fastVal = await cacheService.GetAsync(fastKey);
        Assert.Equal("v2", fastVal);

        // Verify Slow Key NOT Refreshed (需要5秒)
        var slowVal = await cacheService.GetAsync(slowKey);
        Assert.Equal("v1", slowVal);

        await hostedService.StopAsync(CancellationToken.None);
        redis.Dispose();
    }

    /// <summary>
    /// 测试当Redis数据变更时，后台刷新应更新本地缓存
    /// </summary>
    [Fact]
    public async Task BackgroundRefresh_ShouldUpdateLocalCache_WhenRedisChanges()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _redisFixture.ConnectionString;
            options.BackgroundRefresh.Enabled = true;
            options.BackgroundRefresh.Interval = TimeSpan.FromMilliseconds(200);
        });

        services.AddL2CacheRefresh<string, string>();

        var provider = services.BuildServiceProvider();
        
        // 手动启动后台服务
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheRefreshBackgroundService<string, string>>()
            .First();
        
        await hostedService.StartAsync(CancellationToken.None);

        var cacheService = provider.GetRequiredService<ICacheService<string, string>>();
        var serializer = new JsonCacheSerializer();

        // Act (执行)
        // 1. 写入初始值
        var key = "test_key_refresh";
        var value1 = "value1";
        await cacheService.PutAsync(key, value1);

        // 验证 L1 已设置
        var l1Value = await cacheService.GetAsync(key);
        Assert.Equal(value1, l1Value);

        // 2. 直接修改 Redis (模拟外部更新)
        var redis = ConnectionMultiplexer.Connect(_redisFixture.ConnectionString);
        var db = redis.GetDatabase();
        var value2 = "value2";
        await db.StringSetAsync($"String:{key}", serializer.SerializeToString(value2));

        // 3. 等待刷新
        await Task.Delay(1000); // 等待时间超过间隔 (200ms)

        // 4. 再次检查 L1
        // 如果刷新工作正常，GetAsync (读取 L1) 应该返回新值
        var l1ValueNew = await cacheService.GetAsync(key);
        
        // Assert (断言)
        Assert.Equal(value2, l1ValueNew);

        await hostedService.StopAsync(CancellationToken.None);
        redis.Dispose();
    }
}
