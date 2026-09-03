using System.Collections.Concurrent;
using L2Cache.Abstractions.Invalidation;
using L2Cache.Abstractions.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace L2Cache.Invalidation;

/// <summary>
/// 失效消息订阅服务（进程单例）。
/// <para>
/// 启动时订阅失效总线；收到消息后按区域清除本节点 L1 缓存。
/// 通过版本号去重，丢弃乱序/重复消息，避免"旧失效消息覆盖新写入"导致 L1 回退。
/// </para>
/// </summary>
internal sealed class InvalidationSubscriber : BackgroundService
{
    /// <summary>版本跟踪表的上限，超过后不再记录新 Key 的版本（仍执行清除，仅失去乱序去重能力）。</summary>
    private const int MaxTrackedKeys = 10_000;

    private readonly ICacheInvalidationBus _bus;
    private readonly IL1CacheStore _l1;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, long> _seenVersions = new();

    public InvalidationSubscriber(ICacheInvalidationBus bus, IL1CacheStore l1, ILogger<InvalidationSubscriber> logger)
    {
        _bus = bus;
        _l1 = l1;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscribed = await _bus.SubscribeAsync(HandleAsync, stoppingToken).ConfigureAwait(false);
        if (!subscribed)
        {
            _logger.LogWarning("缓存失效订阅未生效，L1 一致性将完全依赖 MaxL1Ttl 兜底过期。");
            return;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }

    internal Task HandleAsync(InvalidationMessage message)
    {
        if (message.Version > 0 && !ShouldProcess(message))
        {
            return Task.CompletedTask;
        }

        _l1.Remove($"{message.CacheName}:{message.Key}");
        return Task.CompletedTask;
    }

    private bool ShouldProcess(InvalidationMessage message)
    {
        var key = $"{message.CacheName}:{message.Key}";

        while (true)
        {
            if (_seenVersions.TryGetValue(key, out var seen))
            {
                if (message.Version <= seen)
                {
                    return false; // 乱序或重复消息，丢弃
                }

                if (_seenVersions.TryUpdate(key, message.Version, seen))
                {
                    return true;
                }
            }
            else
            {
                if (_seenVersions.Count >= MaxTrackedKeys)
                {
                    return true; // 超限，放弃跟踪但仍然清除
                }

                if (_seenVersions.TryAdd(key, message.Version))
                {
                    return true;
                }
            }
        }
    }
}
