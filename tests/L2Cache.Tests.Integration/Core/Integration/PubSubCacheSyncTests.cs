using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.Hosting;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 多节点 Pub/Sub 失效同步测试
/// <para>
/// 创建两个使用【相同区域名】的 <see cref="CacheTestHost"/> 模拟同一集群的两个节点（各自独立 L1、共享 L2 与失效频道）：
/// 节点 A 写入/删除缓存后经 Redis Pub/Sub 广播失效消息，节点 B 应清除各自的 L1 缓存。
/// 另配置较短的 <c>MaxL1Ttl</c> 作为兜底：即使失效消息丢失，L1 过期后也会从 L2 读到最新值。
/// </para>
/// </summary>
public class PubSubCacheSyncTests
{
    [Test]
    public async Task PutAsync_OnNodeA_ShouldInvalidateL1_OnNodeB()
    {
        // Arrange：两个节点使用相同的区域名与频道前缀（独立 L1，共享 L2 与失效频道）
        var cacheName = "sync_" + Guid.NewGuid().ToString("N");
        var channelPrefix = "it-sync-" + Guid.NewGuid().ToString("N");
        var key = "sync-key-" + Guid.NewGuid().ToString("N");

        using var nodeA = CreateNode(cacheName, channelPrefix);
        using var nodeB = CreateNode(cacheName, channelPrefix);

        // 模拟节点启动：测试宿主为裸 ServiceProvider，需手动启动失效订阅服务以激活 Pub/Sub 订阅
        await StartInvalidationSubscribersAsync(nodeA);
        await StartInvalidationSubscribersAsync(nodeB);

        // 等待订阅生效
        await Task.Delay(200);

        // 1. 节点 A 写入初始值
        await nodeA.Client.PutAsync(key, "value-1");

        // 2. 节点 B 读取（填充其 L1）
        var valB1 = await nodeB.Client.GetAsync(key);
        await Assert.That(valB1).IsEqualTo("value-1");

        // 3. 节点 A 覆盖写（应触发 Pub -> 节点 B Sub -> 清除其 L1）
        await nodeA.Client.PutAsync(key, "value-2");

        // 4. 轮询等待失效传播（最多 ~5 秒；即使消息丢失，短 MaxL1Ttl 兜底过期后也会读到新值）
        var valB2 = await WaitForValueAsync(nodeB, key, value => value == "value-2");

        // Assert：节点 B 的 L1 失效后应从 L2 读到新值
        await Assert.That(valB2).IsEqualTo("value-2");
    }

    [Test]
    public async Task EvictAsync_OnNodeA_ShouldInvalidateL1_OnNodeB()
    {
        // Arrange：两个节点使用相同的区域名与频道前缀
        var cacheName = "sync_" + Guid.NewGuid().ToString("N");
        var channelPrefix = "it-sync-" + Guid.NewGuid().ToString("N");
        var key = "sync-key-" + Guid.NewGuid().ToString("N");

        using var nodeA = CreateNode(cacheName, channelPrefix);
        using var nodeB = CreateNode(cacheName, channelPrefix);

        await StartInvalidationSubscribersAsync(nodeA);
        await StartInvalidationSubscribersAsync(nodeB);

        // 等待订阅生效
        await Task.Delay(200);

        // 1. 节点 A 写入初始值
        await nodeA.Client.PutAsync(key, "value-1");

        // 2. 节点 B 读取（填充其 L1）
        var valB1 = await nodeB.Client.GetAsync(key);
        await Assert.That(valB1).IsEqualTo("value-1");

        // 3. 节点 A 删除缓存（应触发 Pub -> 节点 B Sub -> 清除其 L1）
        await nodeA.Client.EvictAsync(key);

        // 4. 轮询等待失效传播（最多 ~5 秒；L2 已删除，节点 B 的 L1 失效后 GetAsync 应返回 null）
        var valB2 = await WaitForValueAsync(nodeB, key, value => value is null);

        // Assert
        await Assert.That(valB2).IsNull();
    }

    /// <summary>创建一个"节点"：与对端节点共享区域名与失效频道前缀，并配置较短的 MaxL1Ttl 作为兜底。</summary>
    private static CacheTestHost CreateNode(string cacheName, string channelPrefix)
        => new(
            GlobalTestSetup.RedisConnectionString,
            cacheName: cacheName,
            configureGlobal: options => options.InvalidationChannelPrefix = channelPrefix,
            configureRegion: region => region.MaxL1Ttl = TimeSpan.FromSeconds(1));

    /// <summary>启动宿主内的失效订阅服务（模拟节点启动，激活 Pub/Sub 订阅）。</summary>
    private static async Task StartInvalidationSubscribersAsync(CacheTestHost node)
    {
        foreach (var hostedService in node.GetService<IEnumerable<IHostedService>>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    /// <summary>轮询读取节点缓存值，直到满足条件或超时（10 次 × 500ms），避免固定 sleep 导致 flaky。</summary>
    private static async Task<string?> WaitForValueAsync(CacheTestHost node, string key, Func<string?, bool> condition)
    {
        string? value = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            value = await node.Client.GetAsync(key);
            if (condition(value))
            {
                return value;
            }

            await Task.Delay(500);
        }

        return value;
    }
}
