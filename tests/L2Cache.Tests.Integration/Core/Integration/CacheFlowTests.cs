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

    /// <summary>
    /// 测试：当 L1 和 L2 都未命中时，GetOrLoadAsync 应回源并回填 L1 和 L2
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_LoadFromSource_And_Fill_L1_L2_When_Both_Miss()
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
        services.AddSingleton<TestFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();

        var key = "full_miss_key";
        var fullKey = $"flow_test:{key}";

        // Ensure clean state
        memoryCache.Remove(fullKey);
        await db.KeyDeleteAsync(fullKey);

        // Act
        var result = await cacheService.GetOrLoadAsync(key);

        // Assert
        await Assert.That(result).IsEqualTo($"db_{key}");
        
        // Verify L1
        await Assert.That(memoryCache.TryGetValue(fullKey, out var l1Value)).IsTrue();
        await Assert.That(l1Value).IsEqualTo($"db_{key}");

        // Verify L2
        var l2Value = await db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsTrue();
        await Assert.That(l2Value.ToString()).Contains($"db_{key}");
    }

    public class TestComplexObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ComplexFlowCacheService : L2CacheService<string, TestComplexObject>
    {
        public ComplexFlowCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, TestComplexObject>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "complex_flow_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<TestComplexObject?> QueryDataAsync(string key)
        {
            return Task.FromResult<TestComplexObject?>(new TestComplexObject
            {
                Id = 1,
                Name = $"name_{key}",
                CreatedAt = new DateTime(2023, 1, 1)
            });
        }
    }

    /// <summary>
    /// 测试：复杂对象序列化
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Handle_ComplexObjects()
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
        services.AddSingleton<ComplexFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<ComplexFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();

        var key = "complex_key";
        var fullKey = $"complex_flow_test:{key}";

        // Act
        var result = await cacheService.GetOrLoadAsync(key);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(1);
        await Assert.That(result.Name).IsEqualTo($"name_{key}");

        // Verify L1
        await Assert.That(memoryCache.TryGetValue(fullKey, out var l1Value)).IsTrue();
        var l1Obj = l1Value as TestComplexObject;
        await Assert.That(l1Obj).IsNotNull();
        await Assert.That(l1Obj!.Name).IsEqualTo($"name_{key}");
        
        // Verify L2
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();
        var l2Value = await db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsTrue();
    }

    /// <summary>
    /// 测试：过期时间是否生效
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Respect_Expiration()
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
        services.AddSingleton<TestFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();

        var key = "expire_key";
        var fullKey = $"flow_test:{key}";
        var expiry = TimeSpan.FromMilliseconds(500);

        // Act 1: Load with short expiry
        await cacheService.GetOrLoadAsync(key, expiry);

        // Verify exists
        await Assert.That(memoryCache.TryGetValue(fullKey, out _)).IsTrue();

        // Wait for expiration
        await Task.Delay(1000);

        // Verify L1 expired
        await Assert.That(memoryCache.TryGetValue(fullKey, out _)).IsFalse();
        
        // Verify L2 expired
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();
        var l2Value = await db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsFalse();
    }

    public class ExceptionFlowCacheService : L2CacheService<string, string>
    {
        public ExceptionFlowCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "exception_flow_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<string?> QueryDataAsync(string key)
        {
            throw new InvalidOperationException("Source failed");
        }
    }

    /// <summary>
    /// 测试：当回源抛出异常时，异常应冒泡
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Propagate_Exception_From_Source()
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
        services.AddSingleton<ExceptionFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<ExceptionFlowCacheService>();

        var key = "error_key";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => 
        {
            await cacheService.GetOrLoadAsync(key);
        });
    }
}
