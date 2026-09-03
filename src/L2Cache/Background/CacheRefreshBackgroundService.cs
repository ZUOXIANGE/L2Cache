using L2Cache.Abstractions;
using L2Cache.Configuration;
using L2Cache.Core;
using L2Cache.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace L2Cache.Background;

/// <summary>
/// 后台缓存刷新服务：轮询 <see cref="CacheKeyTracker{TKey,TValue}"/> 中到期的 Key 并调度刷新。
/// <para>通过 <c>AddCache(...).WithBackgroundRefresh()</c> 启用。</para>
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
internal sealed class CacheRefreshBackgroundService<TKey, TValue> : BackgroundService where TKey : notnull
{
    /// <summary>到期检查的轮询间隔，支持亚秒级刷新周期。</summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);

    private readonly IServiceProvider _serviceProvider;
    private readonly CacheKeyTracker<TKey, TValue> _keyTracker;
    private readonly CacheDescriptor<TKey, TValue> _descriptor;
    private readonly ICacheRefreshPolicy<TKey, TValue> _refreshPolicy;
    private readonly ILogger _logger;

    public CacheRefreshBackgroundService(
        IServiceProvider serviceProvider,
        CacheKeyTracker<TKey, TValue> keyTracker,
        CacheDescriptor<TKey, TValue> descriptor,
        ICacheRefreshPolicy<TKey, TValue> refreshPolicy,
        ILogger<CacheRefreshBackgroundService<TKey, TValue>> logger)
    {
        _serviceProvider = serviceProvider;
        _keyTracker = keyTracker;
        _descriptor = descriptor;
        _refreshPolicy = refreshPolicy;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_descriptor.BackgroundRefresh.Enabled)
        {
            return;
        }

        _keyTracker.IsEnabled = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
                await RefreshDueKeysAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台缓存刷新轮询发生错误。CacheName: {CacheName}", _descriptor.CacheName);
            }
        }
    }

    private async Task RefreshDueKeysAsync(CancellationToken stoppingToken)
    {
        var dueKeys = _keyTracker.GetDueKeys();
        if (!dueKeys.Any())
        {
            return;
        }

        // 每轮创建独立 Scope：回源加载器（如 DbContext 仓储）可能是 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetService<CacheClient<TKey, TValue>>();
        if (client is not ICacheRefreshable<TKey> refreshable)
        {
            return;
        }

        foreach (var key in dueKeys)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await refreshable.RefreshKeyAsync(key, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新缓存 Key 失败。CacheName: {CacheName}, Key: {Key}", _descriptor.CacheName, key);
            }
        }
    }
}
