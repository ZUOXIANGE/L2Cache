using System.Text.Json.Serialization;

namespace L2Cache.Abstractions.Invalidation;

/// <summary>
/// 缓存失效消息。当某节点的 L2 数据变更时，通过失效总线广播给所有节点以清除各自的 L1 缓存。
/// </summary>
/// <param name="CacheName">缓存区域名称。</param>
/// <param name="Key">业务 Key（不含区域前缀）。</param>
/// <param name="Version">发布方节点内的单调递增版本号，用于消费端丢弃乱序/重复消息。</param>
public readonly record struct InvalidationMessage(
    [property: JsonPropertyName("cacheName")] string CacheName,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("version")] long Version);

/// <summary>
/// 缓存失效总线抽象。默认实现为 Redis Pub/Sub；可替换为 Kafka、RabbitMQ 等。
/// <para>
/// 频道命名约定：由实现决定（默认实现为 "{ChannelPrefix}:{CacheName}"）。
/// 消息负载为 <see cref="InvalidationMessage"/> 的 JSON 序列化结果。
/// </para>
/// </summary>
public interface ICacheInvalidationBus
{
    /// <summary>发布失效消息。实现应尽力而为（失败仅记录，不抛出）。</summary>
    Task PublishAsync(InvalidationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅失效消息。进程内只允许一次成功订阅；后续调用应返回失败而不重复订阅。
    /// </summary>
    /// <param name="handler">消息处理器。实现应保证处理器异常不会中断订阅。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> SubscribeAsync(Func<InvalidationMessage, Task> handler, CancellationToken cancellationToken = default);
}
