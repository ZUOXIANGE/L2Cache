using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 多级缓存交互流程测试
/// <para>测试 L1 和 L2 缓存之间的数据同步和回填逻辑</para>
/// </summary>
public class CacheFlowTests
{
    /// <summary>
    /// 测试用的 CacheService，暴露受保护的方法以便验证
    /// </summary>
    public class TestFlowCacheService : L2CacheService<string, string>
    {
        private int _queryDataCount;
        public int QueryDataCount => _queryDataCount;

        public TestFlowCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "flow_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<string?> QueryDataAsync(string key)
        {
            Interlocked.Increment(ref _queryDataCount);
            return Task.FromResult<string?>($"db_{key}");
        }
    }

    /// <summary>
    /// 测试：当 L1 未命中但 L2 命中时，GetAsync 应自动回填 L1
    /// </summary>
    [Test]
    public async Task GetAsync_Should_Populate_L1_When_L2_Hit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
        });

        // 注册测试服务
        services.AddSingleton<TestFlowCacheService>();

        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();

        var key = "l2_hit_key";
        var value = "l2_value";

        // 1. 直接写入 Redis (绕过 L1)
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();
        // 注意：L2CacheService 使用 JSON 序列化，且 key 有前缀
        // 默认序列化器是 JsonCacheSerializer，字符串会带引号
        var fullKey = $"flow_test:{key}";
        await db.StringSetAsync(fullKey, $"\"{value}\"");

        // 验证 L1 为空
        await Assert.That(memoryCache.TryGetValue(fullKey, out _)).IsFalse();

        // Act
        var result = await cacheService.GetAsync(key);

        // Assert
        await Assert.That(result).IsEqualTo(value);

        // 验证 L1 已被回填
        await Assert.That(memoryCache.TryGetValue(fullKey, out var l1Value)).IsTrue();
        await Assert.That(l1Value).IsEqualTo(value);
    }
}
