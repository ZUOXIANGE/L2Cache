using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

public class NullValueCachingTests
{
    public class TestNullCacheService : L2CacheService<string, string>
    {
        private int _queryDataCount;
        public int QueryDataCount => _queryDataCount;

        public TestNullCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "null_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<string?> QueryDataAsync(string key)
        {
            Interlocked.Increment(ref _queryDataCount);
            if (key.StartsWith("null", StringComparison.Ordinal)) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>($"val_{key}");
        }
    }

    [Test]
    public async Task GetOrLoadAsync_Should_Cache_Null_When_Enabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
            options.CacheNullValues = true; // Enable Null Caching
            options.NullValueExpiry = TimeSpan.FromSeconds(5);
        });

        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();

        var key = "null_key_1";
        var fullKey = $"null_test:{key}";

        // Act 1: First Call (Miss -> Load Null -> Cache Null)
        var result1 = await cacheService.GetOrLoadAsync(key);

        // Assert 1
        await Assert.That(result1).IsNull();
        await Assert.That(cacheService.QueryDataCount).IsEqualTo(1);

        // Verify Redis has @@NULL@@
        var redisVal = await db.StringGetAsync(fullKey);
        await Assert.That(redisVal.HasValue).IsTrue();
        await Assert.That(redisVal.ToString()).IsEqualTo("@@NULL@@");

        // Act 2: Second Call (Hit Null Cache -> Return Null without Query)
        var result2 = await cacheService.GetOrLoadAsync(key);

        // Assert 2
        await Assert.That(result2).IsNull();
        await Assert.That(cacheService.QueryDataCount).IsEqualTo(1); // Count should not increase
    }

    [Test]
    public async Task GetOrLoadAsync_Should_NOT_Cache_Null_When_Disabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
            options.CacheNullValues = false; // Disable Null Caching
        });

        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();

        var key = "null_key_2";
        var fullKey = $"null_test:{key}";

        // Act 1: First Call
        var result1 = await cacheService.GetOrLoadAsync(key);

        // Assert 1
        await Assert.That(result1).IsNull();
        await Assert.That(cacheService.QueryDataCount).IsEqualTo(1);

        // Verify Redis does NOT have value
        var redisVal = await db.StringGetAsync(fullKey);
        await Assert.That(redisVal.HasValue).IsFalse();

        // Act 2: Second Call
        var result2 = await cacheService.GetOrLoadAsync(key);

        // Assert 2
        await Assert.That(result2).IsNull();
        await Assert.That(cacheService.QueryDataCount).IsEqualTo(2); // Should query again
    }
}
