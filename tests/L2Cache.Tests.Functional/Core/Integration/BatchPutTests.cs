using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace L2Cache.Tests.Functional.Core.Integration;

[Collection("Shared Test Collection")]
public class BatchPutTests
{
    private readonly RedisTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BatchPutTests(RedisTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

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

    [Fact]
    public async Task BatchPutAsync_ShouldWriteAllKeys()
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
            Assert.Equal(kvp.Value, val);
        }

        // Check batch get
        var batchResult = await cacheService.BatchGetAsync(data.Keys.ToList());
        Assert.Equal(data.Count, batchResult.Count);
        foreach (var kvp in data)
        {
            Assert.Equal(kvp.Value, batchResult[kvp.Key]);
        }
    }

    [Fact]
    public async Task BatchPutAsync_ShouldOverwriteExistingKeys()
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
        Assert.Equal("new_value", val);
    }
}
