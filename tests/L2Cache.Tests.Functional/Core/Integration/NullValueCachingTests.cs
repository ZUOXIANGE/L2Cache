using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Core.Integration;

[Collection("Shared Test Collection")]
public class NullValueCachingTests : BaseIntegrationTest
{
    private readonly RedisTestFixture _fixture;

    public NullValueCachingTests(RedisTestFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    public class TestNullCacheService : L2CacheService<string, string>
    {
        public int QueryDataCount = 0;

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
            Interlocked.Increment(ref QueryDataCount);
            if (key.StartsWith("null")) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>($"val_{key}");
        }
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_Cache_Null_When_Enabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.CacheNullValues = true; // Enable Null Caching
            options.NullValueExpiry = TimeSpan.FromSeconds(5);
        });
        
        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();

        var key = "null_key_1";
        var fullKey = $"null_test:{key}";

        // Act 1: First Call (Miss -> Load Null -> Cache Null)
        var result1 = await cacheService.GetOrLoadAsync(key);

        // Assert 1
        Assert.Null(result1);
        Assert.Equal(1, cacheService.QueryDataCount);

        // Verify Redis has @@NULL@@
        var redisVal = await db.StringGetAsync(fullKey);
        Assert.True(redisVal.HasValue);
        Assert.Equal("@@NULL@@", redisVal.ToString());

        // Act 2: Second Call (Hit Null Cache -> Return Null without Query)
        var result2 = await cacheService.GetOrLoadAsync(key);

        // Assert 2
        Assert.Null(result2);
        Assert.Equal(1, cacheService.QueryDataCount); // Count should not increase
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_NOT_Cache_Null_When_Disabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.CacheNullValues = false; // Disable Null Caching
        });
        
        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();

        var key = "null_key_2";
        var fullKey = $"null_test:{key}";

        // Act 1: First Call
        var result1 = await cacheService.GetOrLoadAsync(key);

        // Assert 1
        Assert.Null(result1);
        Assert.Equal(1, cacheService.QueryDataCount);

        // Verify Redis does NOT have value
        var redisVal = await db.StringGetAsync(fullKey);
        Assert.False(redisVal.HasValue);

        // Act 2: Second Call (Miss again)
        var result2 = await cacheService.GetOrLoadAsync(key);

        // Assert 2
        Assert.Null(result2);
        Assert.Equal(2, cacheService.QueryDataCount); // Count SHOULD increase
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_Respect_NullValueExpiry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.CacheNullValues = true;
            options.NullValueExpiry = TimeSpan.FromMilliseconds(500); // Short expiry
        });
        
        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();

        var key = "null_expiry_key";

        // Act 1: Cache Null
        await cacheService.GetOrLoadAsync(key);
        Assert.Equal(1, cacheService.QueryDataCount);

        // Act 2: Access within expiry (Should hit cache)
        await cacheService.GetOrLoadAsync(key);
        Assert.Equal(1, cacheService.QueryDataCount);

        // Act 3: Wait for expiry
        await Task.Delay(1000);

        // Act 4: Access after expiry (Should query DB again)
        var result = await cacheService.GetOrLoadAsync(key);
        
        // Assert
        Assert.Null(result);
        Assert.Equal(2, cacheService.QueryDataCount);
    }

    [Fact]
    public async Task RefreshKeyAsync_Should_Revalidate_Null_Value()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.CacheNullValues = true;
            options.NullValueExpiry = TimeSpan.FromMinutes(5); // Long expiry
        });
        
        services.AddSingleton<TestNullCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestNullCacheService>();
        var db = ConnectionMultiplexer.Connect(_fixture.ConnectionString).GetDatabase();

        var key = "null_refresh_key";
        var fullKey = $"null_test:{key}";

        // 1. Initial Load (Cache Null)
        await cacheService.GetOrLoadAsync(key);
        Assert.Equal(1, cacheService.QueryDataCount);

        // Verify it's cached as null
        var redisVal = await db.StringGetAsync(fullKey);
        Assert.Equal("@@NULL@@", redisVal.ToString());

        // 2. Call RefreshKeyAsync
        // RefreshKeyAsync will:
        // a. Check L1 (found)
        // b. Check Redis (found "@@NULL@@" -> deserializes to null)
        // c. Since newValue is null, it calls QueryDataAsync(key) again to revalidate
        await cacheService.RefreshKeyAsync(key);

        // Assert: QueryDataCount should increase because RefreshKeyAsync forces a re-check for nulls
        Assert.Equal(2, cacheService.QueryDataCount);
        
        // Verify it's still cached as null (since QueryDataAsync still returns null)
        redisVal = await db.StringGetAsync(fullKey);
        Assert.Equal("@@NULL@@", redisVal.ToString());
    }
}
