using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 批量操作流程测试：L1 / L2 / 回源混合命中场景
/// </summary>
public class BatchFlowTests
{
    /// <summary>
    /// 测试：BatchGetOrLoadAsync 混合命中（L1 命中、L2 命中、回源）应返回完整结果并回填缓存
    /// </summary>
    [Test]
    public async Task BatchGetOrLoadAsync_Should_Handle_Mixed_Hits()
    {
        // Arrange：关闭失效广播，避免 Pub/Sub 异步清除本节点 L1 干扰 L1 命中的测试安排
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureRegion: region => region.PublishInvalidation = false);

        var keyL1 = "k_l1";
        var keyL2 = "k_l2";
        var keyDB = "k_db";
        var keys = new List<string> { keyL1, keyL2, keyDB };

        var fullKeyL1 = host.FullKey(keyL1);
        var fullKeyL2 = host.FullKey(keyL2);
        var fullKeyDB = host.FullKey(keyDB);

        // Setup L1 Hit：先写入（L1+L2），再直接删除 L2，仅保留 L1
        await host.Client.PutAsync(keyL1, $"l1_{keyL1}");
        await host.Db.KeyDeleteAsync(fullKeyL1);

        // Setup L2 Hit：直接写 Redis（JSON 字符串带引号），L1 未命中
        await host.Db.StringSetAsync(fullKeyL2, $"\"l2_{keyL2}\"");

        // Setup DB Hit：keyDB 不在任何缓存层，由 Loader 批量回源

        // Act
        var result = await host.Client.BatchGetOrLoadAsync(keys);

        // Assert：三种来源的值都正确返回
        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[keyL1]).IsEqualTo($"l1_{keyL1}");
        await Assert.That(result[keyL2]).IsEqualTo($"l2_{keyL2}");
        await Assert.That(result[keyDB]).IsEqualTo($"db_{keyDB}");

        // Assert：仅缺失的 keyDB 触发回源
        await Assert.That(host.Counter.LoadedKeys).IsEquivalentTo(new[] { keyDB });

        // Verify Side Effects：keyL2 已回填 L1（删除 L2 后 GetAsync 仍命中）
        await host.Db.KeyDeleteAsync(fullKeyL2);
        var l1ValueL2 = await host.Client.GetAsync(keyL2);
        await Assert.That(l1ValueL2).IsEqualTo($"l2_{keyL2}");

        // keyDB 已回填 L2
        var redisValDB = await host.Db.StringGetAsync(fullKeyDB);
        await Assert.That(redisValDB.HasValue).IsTrue();
        await Assert.That(redisValDB.ToString()).Contains($"db_{keyDB}");

        // keyDB 已回填 L1
        await host.Db.KeyDeleteAsync(fullKeyDB);
        var l1ValueDB = await host.Client.GetAsync(keyDB);
        await Assert.That(l1ValueDB).IsEqualTo($"db_{keyDB}");
    }
}
