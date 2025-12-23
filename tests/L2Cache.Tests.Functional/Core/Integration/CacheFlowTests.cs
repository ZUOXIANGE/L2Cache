using L2Cache.Abstractions;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 多级缓存交互流程测试
/// <para>测试 L1 和 L2 缓存之间的数据同步和回填逻辑</para>
/// </summary>
[Collection("Shared Test Collection")]
public class CacheFlowTests : BaseIntegrationTest
{
    private readonly RedisTestFixture _fixture;

    public CacheFlowTests(RedisTestFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// 测试用的 CacheService，暴露受保护的方法以便验证
    /// </summary>
    public class TestFlowCacheService : L2CacheService<string, string>
    {
        private int _queryDataCount = 0;
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
    [Fact]
    public async Task GetAsync_Should_Populate_L1_When_L2_Hit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
        });
        
        // 注册测试服务
        services.AddSingleton<TestFlowCacheService>();

        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();

        var key = "l2_hit_key";
        var value = "l2_value";
        
        // 1. 直接写入 Redis (绕过 L1)
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();
        // 注意：L2CacheService 使用 JSON 序列化，且 key 有前缀
        // 默认序列化器是 JsonCacheSerializer，字符串会带引号
        var fullKey = $"flow_test:{key}";
        await db.StringSetAsync(fullKey, $"\"{value}\"");

        // 验证 L1 为空
        Assert.False(memoryCache.TryGetValue(fullKey, out _));

        // Act
        var result = await cacheService.GetAsync(key);

        // Assert
        Assert.Equal(value, result);
        
        // 验证 L1 已被回填
        Assert.True(memoryCache.TryGetValue(fullKey, out var l1Value));
        Assert.Equal(value, l1Value);
    }

    /// <summary>
    /// 测试：当 L1 和 L2 都未命中时，GetOrLoadAsync 应调用数据源并回填 L1 和 L2
    /// </summary>
    [Fact]
    public async Task GetOrLoadAsync_Should_Call_Source_And_Populate_Both_When_Miss()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
        });
        services.AddSingleton<TestFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();

        var key = "miss_key";
        var expectedValue = $"db_{key}";
        var fullKey = $"flow_test:{key}";

        // Act
        var result = await cacheService.GetOrLoadAsync(key);

        // Assert
        Assert.Equal(expectedValue, result);
        Assert.Equal(1, cacheService.QueryDataCount);

        // 验证 L1 存在
        Assert.True(memoryCache.TryGetValue(fullKey, out var l1Value));
        Assert.Equal(expectedValue, l1Value);

        // 验证 L2 存在
        var l2ValueRedis = await db.StringGetAsync(fullKey);
        Assert.True(l2ValueRedis.HasValue);
        Assert.Contains(expectedValue, l2ValueRedis.ToString()); // JSON 包含引号
    }

    /// <summary>
    /// 测试：PutAsync 应该同时更新 L1 和 L2
    /// </summary>
    [Fact]
    public async Task PutAsync_Should_Update_Both_L1_And_L2()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
        });
        services.AddSingleton<TestFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();

        var key = "put_key";
        var value = "put_value";
        var fullKey = $"flow_test:{key}";

        // Act
        await cacheService.PutAsync(key, value);

        // Assert
        // L1
        Assert.True(memoryCache.TryGetValue(fullKey, out var l1Value));
        Assert.Equal(value, l1Value);

        // L2
        var l2ValueRedis = await db.StringGetAsync(fullKey);
        Assert.True(l2ValueRedis.HasValue);
        Assert.Contains(value, l2ValueRedis.ToString());
    }
}
