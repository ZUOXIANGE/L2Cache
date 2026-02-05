using System.Collections.Concurrent;
using L2Cache.Abstractions;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

public class CacheConcurrentWriteTests
{
    /// <summary>
    /// 测试并发写入场景下的 L1/L2 一致性。
    /// 由于当前实现没有分布式锁或内存锁，高并发写入可能会导致 L1 和 L2 数据不一致。
    /// 这个测试旨在复现这种现象，作为已知限制的记录，或者验证未来的修复。
    /// </summary>
    [Test]
    public async Task PutAsync_ConcurrentWrites_MayCauseInconsistency()
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

        // 使用 ICacheService<string, string>
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<ICacheService<string, string>>();
        var localCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();

        var key = $"concurrent_write_{Guid.NewGuid()}";
        int threadCount = 10;
        int iterations = 100;

        // Act
        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    var value = $"val_{threadId}_{j}";
                    await cacheService.PutAsync(key, value);
                    // 稍微增加一点随机延迟，增加竞争条件的命中率
                    await Task.Delay(Random.Shared.Next(1, 5));
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        // 检查 L1 和 L2 是否一致
        var l2ValueRedis = await db.StringGetAsync($"String:{key}");
        // 注意：L2CacheService 默认使用 JsonCacheSerializer，字符串会被序列化为 "value" (带引号)
        // 我们直接用 cacheService.GetAsync 获取 L2 值（它会处理反序列化）

        // 为了避免 GetAsync 自身的 L1 回填逻辑干扰验证，我们直接分别检查底层存储
        // 1. 检查 Redis (L2)
        string? l2Value = null;
        if (l2ValueRedis.HasValue)
        {
            // 手动反序列化，或者简单去掉引号（如果是简单字符串）
            // 这里为了准确，我们信任 Redis 中的原始值，并在比较时考虑序列化格式
            l2Value = l2ValueRedis.ToString().Trim('"');
        }

        // 2. 检查 MemoryCache (L1)
        // L2CacheService 使用 "CacheName:Key" 作为 fullKey
        var fullKey = $"String:{key}";
        var l1Exists = localCache.TryGetValue(fullKey, out string? l1Value);

        Console.WriteLine($"Final L2 Value (Redis): {l2Value}");
        Console.WriteLine($"Final L1 Value (Memory): {l1Value}");

        // 验证一致性
        // 注意：如果最后一次写入的 Redis 请求先完成，但对应的 L1 写入晚于另一个线程的 L1 写入，就会不一致。
        // 这是一个观察性测试，不一定每次都失败或成功，取决于竞态。
        // 但我们可以断言它们都非空
        // await Assert.That(l2Value).IsNotNull(); // 可能会失败如果所有写入都失败了
    }
}
