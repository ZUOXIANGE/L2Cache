using L2Cache.Configuration;
using L2Cache.Abstractions;
using L2Cache.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace L2Cache.Background;

public class CacheRefreshBackgroundService<TKey, TValue> : BackgroundService where TKey : notnull
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CacheKeyTracker<TKey, TValue> _keyTracker;
    private readonly L2CacheOptions _options;
    private readonly ILogger _logger;

    public CacheRefreshBackgroundService(
        IServiceProvider serviceProvider,
        CacheKeyTracker<TKey, TValue> keyTracker,
        IOptions<L2CacheOptions> options,
        ILogger<CacheRefreshBackgroundService<TKey, TValue>> logger)
    {
        _serviceProvider = serviceProvider;
        _keyTracker = keyTracker;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.BackgroundRefresh.Enabled)
        {
            return;
        }

        _keyTracker.IsEnabled = true;

        // Use a fixed polling interval (e.g., 100ms) to check for due keys
        // This allows for relatively fast refresh cycles (sub-second) if configured
        var pollingInterval = TimeSpan.FromMilliseconds(100);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(pollingInterval, stoppingToken);

            try
            {
                await RefreshKeysAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during background cache refresh");
            }
        }
    }

    private async Task RefreshKeysAsync(CancellationToken stoppingToken)
    {
        var keys = _keyTracker.GetDueKeys();
        if (!keys.Any()) return;

        using var scope = _serviceProvider.CreateScope();
        var cacheService = scope.ServiceProvider.GetService<ICacheService<TKey, TValue>>();

        if (cacheService is ICacheRefreshable<TKey> refreshableCache)
        {
            foreach (var key in keys)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await refreshableCache.RefreshKeyAsync(key); 
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh key {Key}", key);
                }
            }
        }
    }
}
