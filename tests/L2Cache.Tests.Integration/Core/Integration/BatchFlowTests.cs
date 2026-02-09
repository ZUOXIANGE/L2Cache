using L2Cache.Configuration;
using L2Cache.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

public class BatchFlowTests
{
    public class TestBatchFlowCacheService : L2CacheService<string, string>
    {
        public TestBatchFlowCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "batch_flow_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<Dictionary<string, string>> QueryDataListAsync(List<string> keyList)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keyList)
            {
                result[key] = $"db_{key}";
            }
            return Task.FromResult(result);
        }
    }

    [Test]
    public async Task BatchGetOrLoadAsync_Should_Handle_Mixed_Hits()
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
        services.AddSingleton<TestBatchFlowCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchFlowCacheService>();
        var memoryCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();

        var keyL1 = "k_l1";
        var keyL2 = "k_l2";
        var keyDB = "k_db";
        var keys = new List<string> { keyL1, keyL2, keyDB };

        var fullKeyL1 = $"batch_flow_test:{keyL1}";
        var fullKeyL2 = $"batch_flow_test:{keyL2}";
        var fullKeyDB = $"batch_flow_test:{keyDB}";

        // Clear all
        foreach (var k in new[] { fullKeyL1, fullKeyL2, fullKeyDB })
        {
            memoryCache.Remove(k);
            await db.KeyDeleteAsync(k);
        }

        // Setup L1 Hit
        memoryCache.Set(fullKeyL1, $"db_{keyL1}");

        // Setup L2 Hit (and ensure not in L1)
        await db.StringSetAsync(fullKeyL2, $"\"db_{keyL2}\""); // JSON string
        memoryCache.Remove(fullKeyL2);

        // Setup DB Hit (ensure not in L1 or L2)
        // (Done by Clear all)

        // Act
        var result = await cacheService.BatchGetOrLoadAsync(keys);

        // Assert
        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[keyL1]).IsEqualTo($"db_{keyL1}");
        await Assert.That(result[keyL2]).IsEqualTo($"db_{keyL2}");
        await Assert.That(result[keyDB]).IsEqualTo($"db_{keyDB}");

        // Verify Side Effects
        // KeyL2 should be backfilled to L1
        await Assert.That(memoryCache.TryGetValue(fullKeyL2, out var valL2)).IsTrue();
        await Assert.That(valL2).IsEqualTo($"db_{keyL2}");

        // KeyDB should be backfilled to L1 and L2
        await Assert.That(memoryCache.TryGetValue(fullKeyDB, out var valDB)).IsTrue();
        await Assert.That(valDB).IsEqualTo($"db_{keyDB}");

        var redisValDB = await db.StringGetAsync(fullKeyDB);
        await Assert.That(redisValDB.HasValue).IsTrue();
        await Assert.That(redisValDB.ToString()).Contains($"db_{keyDB}");
    }
}
