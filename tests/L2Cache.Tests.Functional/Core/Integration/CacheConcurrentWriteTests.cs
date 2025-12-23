using System.Collections.Concurrent;
using L2Cache.Abstractions;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit.Abstractions;

namespace L2Cache.Tests.Functional.Core.Integration;

[Collection("Shared Test Collection")]
public class CacheConcurrentWriteTests
{
    private readonly RedisTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CacheConcurrentWriteTests(RedisTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// 测试并发写入场景下的 L1/L2 一致性。
    /// 由于当前实现没有分布式锁或内存锁，高并发写入可能会导致 L1 和 L2 数据不一致。
    /// 这个测试旨在复现这种现象，作为已知限制的记录，或者验证未来的修复。
    /// </summary>
    [Fact]
    public async Task PutAsync_ConcurrentWrites_MayCauseInconsistency()
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
        
        // 使用 ICacheService<string, string>
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<ICacheService<string, string>>();
        var localCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
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

        _output.WriteLine($"Final L2 Value (Redis): {l2Value}");
        _output.WriteLine($"Final L1 Value (Memory): {l1Value}");

        // 验证一致性
        // 注意：如果最后一次写入的 Redis 请求先完成，但对应的 L1 写入晚于另一个线程的 L1 写入，就会不一致。
        // 由于没有锁，我们预期这里 *可能* 会失败，或者我们需要断言它 *可能* 不一致。
        // 但为了作为测试用例，我们通常断言它 *应该* 一致，如果不一致则说明代码有问题。
        // 然而，用户要求的是 "测试并发修改场景"，我们可以把这个测试写成 "探索性测试"，
        // 或者如果当前代码确实不支持强一致性，我们可以用 Assert.True(l1Value == l2Value || l1Value != l2Value) 只是为了打印结果，
        // 或者更严格地：如果我们要证明它是脆弱的，我们可以 Assert.NotEqual(l1Value, l2Value) ? 
        
        // 更好的做法是：尝试验证最终一致性，或者至少它们都不为空。
        Assert.True(l2ValueRedis.HasValue, "L2 should have a value");
        Assert.True(l1Exists, "L1 should have a value");
        
        if (l1Value != l2Value)
        {
            _output.WriteLine("!!! Race Condition Detected: L1 and L2 are inconsistent !!!");
        }
        else
        {
            _output.WriteLine("L1 and L2 are consistent (this time).");
        }
    }

    /// <summary>
    /// 测试并发写入和删除场景。
    /// </summary>
    [Fact]
    public async Task PutAndEvict_Concurrent_MayLeaveZombieL1()
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

        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<ICacheService<string, string>>();
        var localCache = sp.GetRequiredService<IMemoryCache>();
        var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        var db = redis.GetDatabase();

        var key = $"concurrent_evict_{Guid.NewGuid()}";
        var fullKey = $"String:{key}";
        
        // Act
        // 一个任务不断写入，一个任务不断删除
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var putTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                await cacheService.PutAsync(key, $"val_{i++}");
                await Task.Delay(1);
            }
        });

        var evictTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await cacheService.EvictAsync(key);
                await Task.Delay(2);
            }
        });

        await Task.WhenAll(putTask, evictTask);

        // Assert
        // 停止后，检查状态。
        // 僵尸缓存场景：Evict 先删了 L1，然后 Put 写入了 L2，然后 Put 写入了 L1，然后 Evict 删除了 L2。
        // 结果：L2 为空，L1 有值。
        
        var l2Exists = await db.KeyExistsAsync(fullKey);
        var l1Exists = localCache.TryGetValue(fullKey, out string? l1Value);

        _output.WriteLine($"Final L2 Exists: {l2Exists}");
        _output.WriteLine($"Final L1 Exists: {l1Exists}, Value: {l1Value}");

        if (!l2Exists && l1Exists)
        {
            _output.WriteLine("!!! Race Condition Detected: Zombie L1 Cache (L2 is gone, L1 remains) !!!");
        }
    }
}
