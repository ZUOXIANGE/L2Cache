using L2Cache.Abstractions;
using L2Cache.Background;
using L2Cache.Extensions;
using L2Cache.Serializers.Json;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 后台刷新功能测试
/// 测试缓存的后台刷新机制，包括不同的刷新策略和Redis数据变更后的本地缓存更新
/// </summary>
public class BackgroundRefreshTests
{
    /// <summary>
    /// 测试用的刷新策略
    /// 根据Key的前缀返回不同的刷新间隔
    /// </summary>
    private sealed class TestRefreshPolicy : ICacheRefreshPolicy<string, string>
    {
        public TimeSpan? GetRefreshInterval(string key)
        {
            if (key.StartsWith("fast", StringComparison.Ordinal))
            {
                return TimeSpan.FromMilliseconds(200);
            }
            if (key.StartsWith("slow", StringComparison.Ordinal))
            {
                return TimeSpan.FromSeconds(5);
            }
            return null; // 默认值
        }
    }

    /// <summary>
    /// 测试配置了刷新策略时，后台刷新应使用不同的间隔
    /// </summary>
    [Test]
    public async Task BackgroundRefresh_ShouldUseDifferentIntervals_WhenPolicyConfigured()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
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
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();
        // 缓存名称为 "String" (基于 TValue 类型名称)
        // L2CacheService 中的Key格式为 $"{GetCacheName()}:{BuildCacheKey(key)}"

        // 注意：L2CacheService默认CacheName可能是根据TValue类型定的，如果没有重写GetCacheName。
        // 查看源码或推断：通常 L2CacheService<TKey, TValue> 如果没有重写，CacheName 可能是 "String" (TValue.Name)
        // 为了确保准确，最好通过 cacheService.GetCacheName() 获取，但这不在接口里。
        // 假设这里是 "String"。

        await db.StringSetAsync($"String:{fastKey}", serializer.SerializeToString("v2"));
        await db.StringSetAsync($"String:{slowKey}", serializer.SerializeToString("v2"));

        // 3. 等待快速刷新的间隔 (200ms) + 缓冲时间
        await Task.Delay(1000);

        // Verify Fast Key Refreshed (L1 应该已从 Redis 更新)
        var fastVal = await cacheService.GetAsync(fastKey);
        await Assert.That(fastVal).IsEqualTo("v2");

        // Verify Slow Key Not Yet Refreshed (Default interval 10s or Policy 5s)
        var slowVal = await cacheService.GetAsync(slowKey);
        await Assert.That(slowVal).IsEqualTo("v1");

        await hostedService.StopAsync(CancellationToken.None);
    }
}
