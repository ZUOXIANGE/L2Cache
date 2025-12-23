using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 缓存击穿/并发请求测试
/// <para>测试在高并发下请求同一个不存在的Key时的行为</para>
/// </summary>
[Collection("Shared Test Collection")]
public class CacheStampedeTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;
    private readonly RedisTestFixture _fixture;

    public CacheStampedeTests(RedisTestFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    /// <summary>
    /// 测试用的 CacheService，带有延迟模拟
    /// </summary>
    public class TestStampedeCacheService : L2CacheService<string, string>
    {
        private int _queryDataCount = 0;
        public int QueryDataCount => _queryDataCount;

        public TestStampedeCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "stampede_test";
        public override string BuildCacheKey(string key) => key;

        protected override async Task<string?> QueryDataAsync(string key)
        {
            // 模拟 DB 延迟，增加并发竞争窗口
            await Task.Delay(100);
            Interlocked.Increment(ref _queryDataCount);
            return $"db_{key}";
        }
    }

    /// <summary>
    /// 测试：并发调用 GetOrLoadAsync
    /// <para>已启用内存锁和分布式锁。</para>
    /// <para>预期只会有一次回源查询（QueryDataAsync 被调用一次）。</para>
    /// </summary>
    [Fact]
    public async Task GetOrLoadAsync_ConcurrentCalls_WithLocks_ShouldHitSourceOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.Lock.EnabledMemoryLock = true;
            options.Lock.EnabledDistributedLock = true;
            options.Lock.LockTimeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<TestStampedeCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestStampedeCacheService>();

        var key = $"stampede_{Guid.NewGuid()}";
        int concurrentClients = 20;
        var tasks = new List<Task<string?>>();

        // Act
        for (int i = 0; i < concurrentClients; i++)
        {
            tasks.Add(Task.Run(() => cacheService.GetOrLoadAsync(key)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        // 1. 所有请求都应成功并获得相同结果
        Assert.All(results, r => Assert.Equal($"db_{key}", r));

        // 2. 验证回源次数
        // 启用了锁机制，预期回源次数为 1
        _output.WriteLine($"Concurrent requests: {concurrentClients}");
        _output.WriteLine($"Actual Source Queries: {cacheService.QueryDataCount}");
        
        Assert.Equal(1, cacheService.QueryDataCount);
    }
}
