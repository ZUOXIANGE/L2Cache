using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 批量写入测试
/// </summary>
public class BatchPutTests
{
    /// <summary>
    /// 测试：BatchPutAsync 应写入所有 Key（L1 + L2）
    /// </summary>
    [Test]
    public async Task BatchPutAsync_ShouldWriteAllKeys()
    {
        // Arrange：关闭失效广播，避免 Pub/Sub 异步清除本节点 L1 干扰断言
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureRegion: region => region.PublishInvalidation = false);

        var data = new Dictionary<string, string>
        {
            { "k1", "v1" },
            { "k2", "v2" },
            { "k3", "v3" }
        };

        // Act
        await host.Client.BatchPutAsync(data);

        // Assert：逐条读取
        foreach (var kvp in data)
        {
            var val = await host.Client.GetAsync(kvp.Key);
            await Assert.That(val).IsEqualTo(kvp.Value);
        }

        // Assert：批量读取
        var batchResult = await host.Client.BatchGetAsync(data.Keys.ToList());
        await Assert.That(batchResult).Count().IsEqualTo(data.Count);
        foreach (var kvp in data)
        {
            await Assert.That(batchResult[kvp.Key]).IsEqualTo(kvp.Value);
        }

        // Assert：L2 底层数据为 JSON 序列化（字符串带引号）
        foreach (var kvp in data)
        {
            var raw = await host.Db.StringGetAsync(host.FullKey(kvp.Key));
            await Assert.That(raw.HasValue).IsTrue();
            await Assert.That(raw.ToString()).IsEqualTo($"\"{kvp.Value}\"");
        }

        // Assert：L1 已回填（删除 L2 后仍可读取）
        await host.Db.KeyDeleteAsync(host.FullKey("k1"));
        var l1Value = await host.Client.GetAsync("k1");
        await Assert.That(l1Value).IsEqualTo("v1");
    }

    /// <summary>
    /// 测试：BatchPutAsync 应覆盖已存在的 Key
    /// </summary>
    [Test]
    public async Task BatchPutAsync_ShouldOverwriteExistingKeys()
    {
        // Arrange
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureRegion: region => region.PublishInvalidation = false);

        var key = "overwrite_key";
        await host.Client.PutAsync(key, "old_value");

        var data = new Dictionary<string, string>
        {
            { key, "new_value" }
        };

        // Act
        await host.Client.BatchPutAsync(data);

        // Assert
        var val = await host.Client.GetAsync(key);
        await Assert.That(val).IsEqualTo("new_value");

        var raw = await host.Db.StringGetAsync(host.FullKey(key));
        await Assert.That(raw.ToString()).IsEqualTo("\"new_value\"");
    }
}
