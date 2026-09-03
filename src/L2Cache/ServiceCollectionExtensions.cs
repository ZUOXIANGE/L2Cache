using L2Cache.Abstractions;
using L2Cache.Abstractions.Invalidation;
using L2Cache.Abstractions.Policies;
using L2Cache.Abstractions.Stores;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Background;
using L2Cache.Configuration;
using L2Cache.Core;
using L2Cache.Internal;
using L2Cache.Invalidation;
using L2Cache.Stores;
using L2Cache.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

// ReSharper disable once CheckNamespace
namespace L2Cache;

/// <summary>
/// L2Cache 构建器：提供流式注册缓存区域的 API。
/// </summary>
public interface IL2CacheBuilder
{
    /// <summary>服务集合。</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// 注册一个缓存区域。
    /// </summary>
    /// <typeparam name="TKey">业务 Key 类型。</typeparam>
    /// <typeparam name="TValue">缓存值类型。</typeparam>
    /// <param name="cacheName">区域名称（Redis Key 前缀与失效频道后缀）。</param>
    /// <param name="configure">区域配置（TTL、锁、空值策略等）。</param>
    /// <returns>区域构建器，可继续配置 Loader 与后台刷新。</returns>
    IL2CacheRegionBuilder<TKey, TValue> AddCache<TKey, TValue>(string cacheName, Action<CacheRegionOptions>? configure = null) where TKey : notnull;
}

/// <summary>
/// 缓存区域构建器：为该区域配置回源加载器与后台刷新。
/// </summary>
public interface IL2CacheRegionBuilder<TKey, TValue> where TKey : notnull
{
    /// <summary>服务集合。</summary>
    IServiceCollection Services { get; }

    /// <summary>注册回源加载器类型（从 DI 解析，可注入 Scoped 依赖如 DbContext 仓储）。</summary>
    IL2CacheRegionBuilder<TKey, TValue> WithLoader<TLoader>() where TLoader : class, ILoader<TKey, TValue>;

    /// <summary>通过工厂注册回源加载器。</summary>
    IL2CacheRegionBuilder<TKey, TValue> WithLoader(Func<IServiceProvider, ILoader<TKey, TValue>> loaderFactory);

    /// <summary>
    /// 启用后台刷新：L1 中活跃的 Key 将按配置间隔自动刷新（优先采用 L2 最新值，否则回源）。
    /// </summary>
    /// <param name="configure">覆盖区域的后台刷新配置（间隔等）。</param>
    IL2CacheRegionBuilder<TKey, TValue> WithBackgroundRefresh(Action<BackgroundRefreshOptions>? configure = null);
}

/// <summary>
/// L2Cache 服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 L2Cache 核心服务，并返回构建器以继续注册缓存区域。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">全局配置（是否启用 L1/L2、Redis 连接等）。</param>
    public static IL2CacheBuilder AddL2Cache(this IServiceCollection services, Action<L2CacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new L2CacheOptions();
        configure(options);

        services.Configure(configure);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton(options);

        if (options.UseLocalCache)
        {
            services.AddMemoryCache();
            services.TryAddSingleton<IL1CacheStore, MemoryCacheStore>();
        }

        if (options.UseRedis)
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var config = ConfigurationOptions.Parse(options.Redis.ConnectionString);
                return ConnectionMultiplexer.Connect(config);
            });
            services.TryAddSingleton<IL2CacheStore, RedisCacheStore>();
            services.TryAddSingleton<ICacheInvalidationBus, RedisPubSubInvalidationBus>();
        }

        // 遥测：默认 NoOp，可通过 AddL2CacheTelemetry 替换为 DefaultTelemetryProvider
        services.TryAddSingleton(options.Telemetry);
        services.TryAddSingleton<ITelemetryProvider, NoOpTelemetryProvider>();

        services.AddSingleton<CacheOrchestrator>();

        // 失效订阅（进程单例）：同时启用 L1 与 Redis 时才有跨节点 L1 同步需求
        if (options.UseLocalCache && options.UseRedis)
        {
            services.AddHostedService<InvalidationSubscriber>();
        }

        return new L2CacheBuilder(services, options);
    }
}

/// <summary>L2Cache 构建器实现。</summary>
internal sealed class L2CacheBuilder(IServiceCollection services, L2CacheOptions options) : IL2CacheBuilder
{
    public IServiceCollection Services => services;

    public IL2CacheRegionBuilder<TKey, TValue> AddCache<TKey, TValue>(string cacheName, Action<CacheRegionOptions>? configure = null) where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheName);

        var regionOptions = new CacheRegionOptions { CacheName = cacheName };
        configure?.Invoke(regionOptions);

        // 区域描述符（单例）：注册期冻结区域配置与默认策略
        services.AddSingleton(sp => CacheDescriptor<TKey, TValue>.Create(sp, options, regionOptions));

        // 缓存客户端（Scoped）：Loader 可能是 Scoped 服务（如 DbContext 仓储）
        services.AddScoped<CacheClient<TKey, TValue>>();
        services.AddScoped<ICacheClient<TKey, TValue>>(sp => sp.GetRequiredService<CacheClient<TKey, TValue>>());
        services.AddScoped<ICacheRefreshable<TKey>>(sp => sp.GetRequiredService<CacheClient<TKey, TValue>>());

        return new L2CacheRegionBuilder<TKey, TValue>(services, regionOptions);
    }
}

/// <summary>缓存区域构建器实现。</summary>
internal sealed class L2CacheRegionBuilder<TKey, TValue>(IServiceCollection services, CacheRegionOptions regionOptions) : IL2CacheRegionBuilder<TKey, TValue> where TKey : notnull
{
    public IServiceCollection Services => services;

    public IL2CacheRegionBuilder<TKey, TValue> WithLoader<TLoader>() where TLoader : class, ILoader<TKey, TValue>
    {
        services.AddScoped<ILoader<TKey, TValue>, TLoader>();
        return this;
    }

    public IL2CacheRegionBuilder<TKey, TValue> WithLoader(Func<IServiceProvider, ILoader<TKey, TValue>> loaderFactory)
    {
        services.AddScoped(loaderFactory);
        return this;
    }

    public IL2CacheRegionBuilder<TKey, TValue> WithBackgroundRefresh(Action<BackgroundRefreshOptions>? configure = null)
    {
        var background = regionOptions.BackgroundRefresh;
        background.Enabled = true;
        configure?.Invoke(background);

        // Key 跟踪器与刷新策略均为单例，可被 CacheDescriptor 解析
        services.TryAddSingleton<CacheKeyTracker<TKey, TValue>>();
        services.TryAddSingleton<ICacheRefreshPolicy<TKey, TValue>, DefaultCacheRefreshPolicy<TKey, TValue>>();
        services.AddHostedService<CacheRefreshBackgroundService<TKey, TValue>>();

        return this;
    }
}
