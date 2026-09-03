using L2Cache.Abstractions;
using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 弹性/韧性测试
/// <para>
/// 新架构下 L2 存储实现应容忍 Redis 连接故障（读失败视为未命中、写失败不抛异常）。
/// 容器级故障注入难以稳定复现，这里验证 Redis 正常运行时数据被外部删除/破坏后的降级行为，
/// 保证测试简单确定性。
/// </para>
/// </summary>
public class ResilienceTests
{
    /// <summary>
    /// 测试：L2 中数据被外部删除后，客户端应降级为回源，仍正常工作
    /// </summary>
    [Test]
    public async Task Client_Should_Degrade_Gracefully_When_L2_Data_Removed_Externally()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);
        var client = host.Client;
        var key = "resilience_removed_key";

        // 写入缓存（L1 + L2）
        await client.PutAsync(key, "v1");
        await Assert.That(await host.Db.KeyExistsAsync(host.FullKey(key))).IsTrue();

        // 外部直接删除 L2（模拟 Redis 数据丢失）
        await host.Db.KeyDeleteAsync(host.FullKey(key));

        // 清除 L1 后重新获取：L1 / L2 均未命中，应自动回源
        await client.EvictAsync(key);

        // Act (执行)
        var reloaded = await client.GetOrLoadAsync(key);

        // Assert (断言)：回源恢复正常
        await Assert.That(reloaded).IsEqualTo($"db_{key}");
        await Assert.That(host.Counter.LoadCount).IsEqualTo(1);
    }

    /// <summary>
    /// 测试：对不存在的 Key 执行淘汰操作应返回 false / 0，不抛异常
    /// </summary>
    [Test]
    public async Task Evict_Should_Return_False_Or_Zero_For_Missing_Keys()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);
        var client = host.Client;

        // Act (执行)
        var evicted = await client.EvictAsync("missing_key");
        var batchEvicted = await client.BatchEvictAsync(["missing_key_1", "missing_key_2"]);

        // Assert (断言)
        await Assert.That(evicted).IsFalse();
        await Assert.That(batchEvicted).IsEqualTo(0L);
    }

    /// <summary>
    /// 测试：查询未命中的 Key 应返回 null / false / 空集合，不抛异常
    /// </summary>
    [Test]
    public async Task Get_Miss_Should_Return_Default_Without_Exception()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);
        var client = host.Client;

        // Act (执行)
        var value = await client.GetAsync("never_exists");
        var exists = await client.ExistsAsync("never_exists");
        var batch = await client.BatchGetAsync(["never_exists_1", "never_exists_2"]);

        // Assert (断言)
        await Assert.That(value).IsNull();
        await Assert.That(exists).IsFalse();
        await Assert.That(batch).IsEmpty();
    }
}
