using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// Redis缓存设置钩子测试
/// 测试在写入Redis缓存时的回调机制
/// </summary>
[Collection("Shared Test Collection")]
public class OnRedisCacheSetTests
{
    private readonly RedisTestFixture _redisFixture;

    public OnRedisCacheSetTests(RedisTestFixture redisFixture)
    {
        _redisFixture = redisFixture;
    }

    /// <summary>
    /// 带有钩子的测试缓存服务
    /// 继承自 L2CacheService 并重写 OnRedisCacheSet 方法
    /// </summary>
    public class TestCacheServiceWithHook : L2CacheService<string, string>
    {
        public bool OnRedisCacheSetCalled { get; private set; }
        public string? LastSetKey { get; private set; }
        public string? LastSetValue { get; private set; }
        public TimeSpan? LastSetExpiry { get; private set; }

        public TestCacheServiceWithHook(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "test_hook";
        public override string BuildCacheKey(string key) => key;
        protected override Task<string?> QueryDataAsync(string key) => Task.FromResult<string?>("db_value");

        protected override void OnRedisCacheSet(string key, string value, TimeSpan? expiry)
        {
            OnRedisCacheSetCalled = true;
            LastSetKey = key;
            LastSetValue = value;
            LastSetExpiry = expiry;
        }
    }

    /// <summary>
    /// 测试当调用 PutAsync 时，OnRedisCacheSet 应该被调用
    /// </summary>
    [Fact]
    public async Task OnRedisCacheSet_ShouldBeCalled_WhenPutAsyncIsCalled()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _redisFixture.ConnectionString;
        });
        
        // 注册测试服务
        services.AddSingleton<TestCacheServiceWithHook>();

        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestCacheServiceWithHook>();

        var key = "hook_test_key";
        var value = "hook_test_value";
        var expiry = TimeSpan.FromMinutes(5);

        // Act (执行)
        await cacheService.PutAsync(key, value, expiry);

        // Assert (断言)
        Assert.True(cacheService.OnRedisCacheSetCalled, "OnRedisCacheSet 应该被调用");
        Assert.Equal(key, cacheService.LastSetKey);
        Assert.Equal(value, cacheService.LastSetValue);
        Assert.Equal(expiry, cacheService.LastSetExpiry);
    }
}
