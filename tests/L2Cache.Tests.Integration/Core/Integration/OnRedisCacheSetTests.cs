using System.Collections.Concurrent;
using System.Text.Json;
using L2Cache.Tests.Integration.Helpers;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// Redis 缓存写入事件测试
/// <para>
/// "写缓存触发事件"对应两方面的验证：
/// 1. PutAsync 应将 JSON 序列化后的值与指定的 TTL 写入 L2（Redis）；
/// 2. PutAsync 应向失效频道（"{Prefix}:{CacheName}"）发布失效消息，通知其他节点清除各自的 L1。
/// </para>
/// </summary>
public class OnRedisCacheSetTests
{
    /// <summary>
    /// 测试当调用 PutAsync 时，L2 应写入 JSON 序列化值并应用指定的过期时间
    /// </summary>
    [Test]
    public async Task PutAsync_Should_Write_JsonValue_With_Ttl_To_L2()
    {
        // Arrange（准备）
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = "hook_test_key";
        var value = "hook_test_value";
        var expiry = TimeSpan.FromMinutes(5);
        var fullKey = host.FullKey(key);
        // L2 值为 JSON 序列化，字符串带引号
        var expectedJson = $"\"{value}\"";

        // Act（执行）
        await host.Client.PutAsync(key, value, expiry);

        // Assert（断言）：L2 中为 JSON 序列化后的值
        var redisVal = await host.Db.StringGetAsync(fullKey);
        await Assert.That(redisVal.HasValue).IsTrue();
        await Assert.That(redisVal.ToString()).IsEqualTo(expectedJson);

        // Assert：缓存读取应命中刚写入的值
        await Assert.That(await host.Client.GetAsync(key)).IsEqualTo(value);

        // Assert：L2 TTL 应为指定的过期时间（留出执行耗时余量）
        var ttl = await host.Db.KeyTimeToLiveAsync(fullKey);
        await Assert.That(ttl.HasValue).IsTrue();
        await Assert.That(ttl!.Value).IsGreaterThan(TimeSpan.FromMinutes(4));
        await Assert.That(ttl.Value).IsLessThanOrEqualTo(expiry);
    }

    /// <summary>
    /// 测试当调用 PutAsync 时，应向失效频道发布失效消息（写缓存触发事件）
    /// </summary>
    [Test]
    public async Task PutAsync_Should_Publish_Invalidation_Message()
    {
        // Arrange（准备）：使用独立频道前缀，避免与其他测试的失效消息冲突
        var channelPrefix = "it-hook-" + Guid.NewGuid().ToString("N");
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureGlobal: options => options.InvalidationChannelPrefix = channelPrefix);

        var messages = new ConcurrentQueue<string>();
        var channel = RedisChannel.Literal($"{channelPrefix}:{host.CacheName}");
        await host.Connection.GetSubscriber().SubscribeAsync(channel, (_, payload) =>
        {
            if (payload.HasValue)
            {
                messages.Enqueue(payload.ToString());
            }
        });

        // 等待订阅生效
        await Task.Delay(200);

        var key = "hook_event_key";
        var value = "hook_event_value";

        // Act（执行）：写缓存应触发失效广播
        await host.Client.PutAsync(key, value);

        // Assert（断言）：轮询等待失效消息到达（最多 ~5 秒，避免固定 sleep 导致 flaky）
        var message = await WaitForMessageAsync(messages, host.CacheName, key);
        await Assert.That(message).IsNotNull();

        using var doc = JsonDocument.Parse(message!);
        await Assert.That(doc.RootElement.GetProperty("cacheName").GetString()).IsEqualTo(host.CacheName);
        await Assert.That(doc.RootElement.GetProperty("key").GetString()).IsEqualTo(key);
    }

    /// <summary>轮询已收到的失效消息，直到出现匹配 cacheName 与 key 的消息或超时（5 秒）。</summary>
    private static async Task<string?> WaitForMessageAsync(ConcurrentQueue<string> messages, string cacheName, string key)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            while (messages.TryDequeue(out var message))
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("cacheName", out var cacheNameProp) &&
                    cacheNameProp.GetString() == cacheName &&
                    doc.RootElement.TryGetProperty("key", out var keyProp) &&
                    keyProp.GetString() == key)
                {
                    return message;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(200);
        }
    }
}
