using System.Collections.Concurrent;
using System.Text.Json;
using L2Cache.Abstractions.Invalidation;
using L2Cache.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Invalidation;

/// <summary>
/// 基于 Redis Pub/Sub 的失效总线实现。
/// <para>
/// 频道命名："{InvalidationChannelPrefix}:{CacheName}"，订阅端使用 "{Prefix}:*" 模式订阅。
/// 消息负载为 <see cref="InvalidationMessage"/> 的 JSON。
/// </para>
/// </summary>
internal sealed class RedisPubSubInvalidationBus : ICacheInvalidationBus
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly string _channelPrefix;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, string> _channels = new();

    public RedisPubSubInvalidationBus(IConnectionMultiplexer multiplexer, L2CacheOptions options, ILogger<RedisPubSubInvalidationBus> logger)
    {
        _multiplexer = multiplexer;
        _channelPrefix = options.InvalidationChannelPrefix;
        _logger = logger;
    }

    /// <summary>按 CacheName 缓存频道名，避免每次发布重新拼接字符串（区域集合有限）。</summary>
    private string GetChannel(string cacheName) => _channels.GetOrAdd(cacheName, static (name, prefix) => $"{prefix}:{name}", _channelPrefix);

    public async Task PublishAsync(InvalidationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
            await _multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(GetChannel(message.CacheName)), payload, CommandFlags.FireAndForget).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 发布失败不影响主流程：其他节点的 L1 依赖 MaxL1Ttl 兜底过期
            _logger.LogWarning(ex, "发布缓存失效消息失败。CacheName: {CacheName}, Key: {Key}", message.CacheName, message.Key);
        }
    }

    public async Task<bool> SubscribeAsync(Func<InvalidationMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = RedisChannel.Pattern($"{_channelPrefix}:*");
            await _multiplexer.GetSubscriber().SubscribeAsync(pattern, (channel, payload) =>
            {
                _ = InvokeHandlerAsync(handler, payload);
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "订阅缓存失效频道失败。Prefix: {Prefix}", _channelPrefix);
            return false;
        }
    }

    private async Task InvokeHandlerAsync(Func<InvalidationMessage, Task> handler, RedisValue payload)
    {
        try
        {
            if (!payload.HasValue)
            {
                return;
            }

            // 直接对字节反序列化，避免 payload.ToString() 的中间字符串分配
            var bytes = (byte[]?)payload;
            if (bytes is null || bytes.Length == 0)
            {
                return;
            }

            var message = JsonSerializer.Deserialize<InvalidationMessage>(bytes, SerializerOptions);
            if (string.IsNullOrEmpty(message.CacheName) || string.IsNullOrEmpty(message.Key))
            {
                return;
            }

            await handler(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 处理失败仅记录：消费端 L1 依赖 MaxL1Ttl 兜底过期
            _logger.LogWarning(ex, "处理缓存失效消息失败");
        }
    }
}
