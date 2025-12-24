using L2Cache.Abstractions;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Background;
using L2Cache.Configuration;
using L2Cache.Internal;
using L2Cache.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace L2Cache;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// AddL2Cache
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static IServiceCollection AddL2Cache(this IServiceCollection services, Action<L2CacheOptions> configure)
    {
        var options = new L2CacheOptions();
        configure(options);

        // Register options
        services.Configure(configure);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Register Local Cache
        if (options.UseLocalCache)
        {
            services.AddMemoryCache();
        }

        // Register Redis
        if (options.UseRedis)
        {
            // Manually register ConnectionMultiplexer to match options
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var config = ConfigurationOptions.Parse(options.Redis.ConnectionString);
                // Apply other redis options if needed
                return ConnectionMultiplexer.Connect(config);
            });

            services.AddScoped<IDatabase>(sp =>
            {
                var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                return multiplexer.GetDatabase(options.Redis.Database);
            });
        }

        // Register Telemetry Options
        services.AddSingleton<TelemetryOptions>(options.Telemetry);

        // Register Default NoOp Telemetry Provider (can be overridden by L2Cache.Telemetry)
        services.TryAddSingleton<ITelemetryProvider, NoOpTelemetryProvider>();

        // Register Generic Cache Service
        services.AddScoped(typeof(ICacheService<,>), typeof(L2CacheService<,>));

        // Register Cache Key Tracker (Open Generic Singleton)
        services.AddSingleton(typeof(CacheKeyTracker<,>));

        return services;
    }

    /// <summary>
    /// 启用指定类型的后台缓存刷新
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configurePolicy">可选的策略配置，用于自定义Key的刷新间隔</param>
    public static IServiceCollection AddL2CacheRefresh<TKey, TValue>(this IServiceCollection services, Func<IServiceProvider, ICacheRefreshPolicy<TKey, TValue>>? configurePolicy = null)
        where TKey : notnull
    {
        services.AddHostedService<CacheRefreshBackgroundService<TKey, TValue>>();

        if (configurePolicy != null)
        {
            services.AddSingleton(configurePolicy);
        }
        else
        {
            services.AddSingleton<ICacheRefreshPolicy<TKey, TValue>, DefaultCacheRefreshPolicy<TKey, TValue>>();
        }

        return services;
    }
}