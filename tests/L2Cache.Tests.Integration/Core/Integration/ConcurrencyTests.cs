using L2Cache.Abstractions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 并发测试
/// 测试在高并发场景下的缓存读写一致性和稳定性
/// </summary>
public class ConcurrencyTests
{
    /// <summary>
    /// 测试并发读取应返回一致的结果
    /// </summary>
    [Test]
    public async Task Concurrent_Get_Should_Return_Consistent_Result()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
        });

        // 我们需要一个具体的实现来测试，这里可以使用 L2CacheService 的简单实现
        // 或者使用 Mock，但集成测试最好用真实的 Service 类（虽然抽象类不能直接实例化）
        // 这里我们可以复用 TestHelpers 中定义的或者简单的匿名类？
        // 为了简单，我们定义一个内部类
        services.AddSingleton<TestConcurrencyCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestConcurrencyCacheService>();

        var key = $"concurrent_get_{Guid.NewGuid()}";
        var expectedValue = "initial_value";

        // 预热缓存
        await cacheService.PutAsync(key, expectedValue);

        // Act (执行)
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () => await cacheService.GetAsync(key)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert (断言)
        foreach (var result in results)
        {
            await Assert.That(result).IsEqualTo(expectedValue);
        }
    }

    /// <summary>
    /// 测试并发写入不应导致崩溃
    /// </summary>
    [Test]
    public async Task Concurrent_Put_Should_Not_Crash()
    {
        // Arrange (准备)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
        });
        services.AddSingleton<TestConcurrencyCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<TestConcurrencyCacheService>();

        var key = $"concurrent_put_{Guid.NewGuid()}";

        // Act (执行)
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            var value = $"value_{i}";
            tasks.Add(Task.Run(async () => await cacheService.PutAsync(key, value)));
        }

        Func<Task> act = async () => await Task.WhenAll(tasks);

        // Assert (断言)
        await act();

        // 最终值检查 (应该是其中一个值)
        var finalValue = await cacheService.GetAsync(key);
        await Assert.That(finalValue).IsNotNull();
        await Assert.That(finalValue).StartsWith("value_");
    }

    public class TestConcurrencyCacheService : L2CacheService<string, string>
    {
        public TestConcurrencyCacheService(
            IServiceProvider sp,
            Microsoft.Extensions.Options.IOptions<L2Cache.Configuration.L2CacheOptions> opts,
            Microsoft.Extensions.Logging.ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "concurrency_test";
        public override string BuildCacheKey(string key) => key;

        protected override Task<string?> QueryDataAsync(string key) => Task.FromResult<string?>(null);
        protected override Task<Dictionary<string, string>> QueryDataListAsync(List<string> keyList) => Task.FromResult(new Dictionary<string, string>());
    }
}
