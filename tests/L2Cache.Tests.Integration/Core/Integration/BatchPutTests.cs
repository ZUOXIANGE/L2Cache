using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace L2Cache.Tests.Integration.Core.Integration;

public class BatchPutTests
{
    public class TestBatchCacheService : L2CacheService<string, string>
    {
        public TestBatchCacheService(
            IServiceProvider sp,
            Microsoft.Extensions.Options.IOptions<L2Cache.Configuration.L2CacheOptions> opts,
            Microsoft.Extensions.Logging.ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "batch_put_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<string?> QueryDataAsync(string key)
        {
            return Task.FromResult<string?>($"val_{key}");
        }

        protected override Task<Dictionary<string, string>> QueryDataListAsync(List<string> keyList)
        {
            return Task.FromResult(keyList.ToDictionary(k => k, k => $"val_{k}"));
        }
    }

    [Test]
    public async Task BatchPutAsync_ShouldWriteAllKeys()
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
        services.AddSingleton<TestBatchCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchCacheService>();

        var data = new Dictionary<string, string>
        {
            { $"k1_{Guid.NewGuid()}", "v1" },
            { $"k2_{Guid.NewGuid()}", "v2" },
            { $"k3_{Guid.NewGuid()}", "v3" }
        };

        // Act
        await cacheService.BatchPutAsync(data);

        // Assert
        // Check individually
        foreach (var kvp in data)
        {
            var val = await cacheService.GetAsync(kvp.Key);
            await Assert.That(val).IsEqualTo(kvp.Value);
        }

        // Check batch get
        var batchResult = await cacheService.BatchGetAsync(data.Keys.ToList());
        await Assert.That(batchResult).Count().IsEqualTo(data.Count);
        foreach (var kvp in data)
        {
            await Assert.That(batchResult[kvp.Key]).IsEqualTo(kvp.Value);
        }
    }

    [Test]
    public async Task BatchPutAsync_ShouldOverwriteExistingKeys()
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
        services.AddSingleton<TestBatchCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestBatchCacheService>();

        var key = $"overwrite_key_{Guid.NewGuid()}";
        await cacheService.PutAsync(key, "old_value");

        var data = new Dictionary<string, string>
        {
            { key, "new_value" }
        };

        // Act
        await cacheService.BatchPutAsync(data);

        // Assert
        var val = await cacheService.GetAsync(key);
        await Assert.That(val).IsEqualTo("new_value");
    }
}
