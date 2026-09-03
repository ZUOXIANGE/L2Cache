using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 并发测试
/// <para>测试在高并发场景下的缓存读写一致性和稳定性。</para>
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
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = $"concurrent_get_{Guid.NewGuid():N}";
        var expectedValue = "initial_value";

        // 预热缓存（写管道会同时更新 L1 与 L2）
        await host.Client.PutAsync(key, expectedValue);

        // Act (执行)
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => host.Client.GetAsync(key)));
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
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = $"concurrent_put_{Guid.NewGuid():N}";

        // Act (执行)：写管道加锁防并发写冲突，全部写入完成后不应抛出异常
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            var value = $"value_{i}";
            tasks.Add(Task.Run(() => host.Client.PutAsync(key, value)));
        }

        await Task.WhenAll(tasks);

        // Assert (断言)
        // 最终值检查（应该是其中一个值）
        var finalValue = await host.Client.GetAsync(key);
        await Assert.That(finalValue).IsNotNull();
        await Assert.That(finalValue).StartsWith("value_");
    }
}
