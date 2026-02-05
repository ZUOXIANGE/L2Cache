using System.Collections.Concurrent;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 批量操作并发测试
/// </summary>
public class BatchConcurrencyTests
{
    public class TestBatchCacheService : L2CacheService<string, string>
    {
        private int _queryListCount;
        public int QueryListCount => _queryListCount;

        // Track individual key queries if QueryDataListAsync is not used or splits calls
        private int _querySingleCount;
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

    [Test]
    public async Task BatchGetOrLoadAsync_ConcurrentCalls_ShouldHaveConsistentResults_ButMayCauseStampede()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
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
            await Assert.That(result).Count().IsEqualTo(keys.Count);
            foreach (var key in keys)
            {
                await Assert.That(result.ContainsKey(key)).IsTrue();
                await Assert.That(result[key]).IsEqualTo($"val_{key}");
            }
        }

        // 验证并发行为
        // 注意：由于没有分布式锁来锁定批量获取，可能会多次调用 QueryDataListAsync
        // 但这里只要保证结果一致性即可
    }
}
