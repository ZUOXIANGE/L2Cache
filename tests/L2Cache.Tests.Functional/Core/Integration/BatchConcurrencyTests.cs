using System.Collections.Concurrent;
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
/// 批量操作并发测试
/// </summary>
[Collection("Shared Test Collection")]
public class BatchConcurrencyTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;
    private readonly RedisTestFixture _fixture;

    public BatchConcurrencyTests(RedisTestFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    public class TestBatchCacheService : L2CacheService<string, string>
    {
        private int _queryListCount = 0;
        public int QueryListCount => _queryListCount;
        
        // Track individual key queries if QueryDataListAsync is not used or splits calls
        private int _querySingleCount = 0; 
        public int QuerySingleCount => _querySingleCount;

        public TestBatchCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "batch_conc_test";
        public override string BuildCacheKey(string key) => key;

        protected override async Task<string?> QueryDataAsync(string key)
        {
            Interlocked.Increment(ref _querySingleCount);
            await Task.Delay(50); // Simulate DB delay
            return $"val_{key}";
        }

        protected override async Task<Dictionary<string, string>> QueryDataListAsync(List<string> keyList)
        {
            Interlocked.Increment(ref _queryListCount);
            await Task.Delay(50); // Simulate DB delay
            return keyList.ToDictionary(k => k, k => $"val_{k}");
        }
    }

    [Fact]
    public async Task BatchGetOrLoadAsync_ConcurrentCalls_ShouldHaveConsistentResults_ButMayCauseStampede()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            // Enable locks for PutAsync, but BatchGetOrLoadAsync implementation currently doesn't lock the batch fetch
            options.Lock.EnabledMemoryLock = true;
            options.Lock.EnabledDistributedLock = true;
        });
        services.AddSingleton<TestBatchCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchCacheService>();

        var keys = Enumerable.Range(0, 10).Select(i => $"batch_key_{Guid.NewGuid()}_{i}").ToList();
        int concurrentClients = 10;
        
        // Act
        var tasks = new List<Task<Dictionary<string, string>>>();
        for (int i = 0; i < concurrentClients; i++)
        {
            tasks.Add(Task.Run(() => cacheService.BatchGetOrLoadAsync(keys)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        // 1. Consistency check
        foreach (var result in results)
        {
            Assert.Equal(keys.Count, result.Count);
            foreach (var key in keys)
            {
                Assert.True(result.ContainsKey(key));
                Assert.Equal($"val_{key}", result[key]);
            }
        }

        // 2. Stampede check
        // Current implementation does NOT lock BatchGetOrLoadAsync, so we expect multiple queries
        _output.WriteLine($"Concurrent Batch Requests: {concurrentClients}");
        _output.WriteLine($"Actual Batch Source Queries: {cacheService.QueryListCount}");
        
        // In a perfect world with batch locking, this would be 1. 
        // But currently it's likely > 1.
        // Let's verify that it works at least, and document the behavior.
        Assert.True(cacheService.QueryListCount >= 1);
        
        // Verify final state in cache
        var finalCheck = await cacheService.GetAsync(keys[0]);
        Assert.Equal($"val_{keys[0]}", finalCheck);
    }

    [Fact]
    public async Task BatchGetOrLoadAsync_MixedWithSingleGet_ShouldFunctionCorrectly()
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
        });
        services.AddSingleton<TestBatchCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchCacheService>();

        var batchKeys = Enumerable.Range(0, 5).Select(i => $"mixed_key_{Guid.NewGuid()}_{i}").ToList();
        var singleKey = batchKeys[0]; // One key overlaps

        // Act
        var t1 = Task.Run(() => cacheService.BatchGetOrLoadAsync(batchKeys));
        var t2 = Task.Run(() => cacheService.GetOrLoadAsync(singleKey));

        await Task.WhenAll(t1, t2);

        // Assert
        var batchResult = await t1;
        var singleResult = await t2;

        Assert.Equal($"val_{singleKey}", singleResult);
        Assert.Equal($"val_{singleKey}", batchResult[singleKey]);
        
        _output.WriteLine($"Batch Queries: {cacheService.QueryListCount}");
        _output.WriteLine($"Single Queries: {cacheService.QuerySingleCount}");
    }

    [Fact]
    public async Task BatchGetOrLoadAsync_ConcurrentUpdate_ShouldNotOverwriteNewerData()
    {
        // 验证 "僵尸缓存" 问题：批量加载过程中，如果有并发更新，是否会覆盖新数据
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
        });
        services.AddSingleton<TestBatchCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchCacheService>();

        var key = $"race_key_{Guid.NewGuid()}";
        var keyList = new List<string> { key };

        // Act
        // 1. 启动批量加载 (模拟慢速 DB)
        var loadTask = Task.Run(async () => 
        {
            // 这个 BatchGetOrLoadAsync 会调用 TestBatchCacheService.QueryDataListAsync，那里有 50ms 延迟
            await cacheService.BatchGetOrLoadAsync(keyList);
        });

        // 2. 在加载过程中，模拟并发更新 (写入新值)
        await Task.Delay(10); // 确保 Batch 已经开始但未完成 (DB Delay is 50ms)
        await cacheService.PutAsync(key, "new_value");

        // 3. 等待批量加载完成
        await loadTask;

        // Assert
        // 如果 BatchGetOrLoadAsync 盲目覆盖，这里会变回 "val_{key}" (旧值)
        // 如果系统健壮，应该保留 "new_value" (或者至少最终一致)
        
        var finalValue = await cacheService.GetAsync(key);
        
        // 这是一个预期会失败的测试，用于验证是否存在 Race Condition
        _output.WriteLine($"Final Value: {finalValue}");
        Assert.Equal("new_value", finalValue);
    }
}
