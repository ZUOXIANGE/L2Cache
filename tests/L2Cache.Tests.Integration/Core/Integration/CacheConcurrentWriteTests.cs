using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 并发写入测试
/// <para>
/// 写管道会对每个写操作加锁（内存锁 + 分布式锁，超时降级为无锁直写），
/// 再执行"写 L2 → 写 L1 → 广播失效"，防止并发写冲突导致 L1/L2 不一致。
/// 本测试验证高并发写入后 L1 与 L2 的最终一致性（对旧实现已知问题的回归验证）。
/// </para>
/// </summary>
public class CacheConcurrentWriteTests
{
    /// <summary>
    /// 测试并发写入场景下的 L1/L2 一致性：
    /// 多线程高并发写入同一个 Key 不应失败，且完成后 L1（客户端读取）与 L2（Redis 原始值）应一致。
    /// </summary>
    [Test]
    public async Task PutAsync_ConcurrentWrites_ShouldKeepL1AndL2Consistent()
    {
        // Arrange
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = $"concurrent_write_{Guid.NewGuid():N}";
        int threadCount = 10;
        int iterations = 20;

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
                    await host.Client.PutAsync(key, value);
                    // 稍微增加一点随机延迟，增加竞争条件的命中率
                    await Task.Delay(Random.Shared.Next(1, 5));
                }
            }));
        }

        // 全部写入完成后不应抛出异常
        await Task.WhenAll(tasks);

        // Assert
        // 1. 直接读取 Redis（L2）原始值
        // 注意：L2 值为 JSON 序列化，字符串会被序列化为 "value"（带引号）
        var l2Raw = await host.Db.StringGetAsync(host.FullKey(key));
        await Assert.That(l2Raw.HasValue).IsTrue();
        var l2Value = l2Raw.ToString().Trim('"');
        await Assert.That(l2Value).StartsWith("val_");

        // 2. 客户端读取（优先 L1，未命中回落 L2）应与 L2 原始值一致
        var finalValue = await host.Client.GetAsync(key);
        await Assert.That(finalValue).IsEqualTo(l2Value);

        Console.WriteLine($"Final L2 Value (Redis): {l2Value}");
        Console.WriteLine($"Final Client Value (L1/L2): {finalValue}");
    }
}
