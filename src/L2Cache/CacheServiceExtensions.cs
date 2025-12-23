using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace L2Cache.Extensions;

/// <summary>
/// 缓存服务扩展类
/// 用于简化依赖注入配置
/// </summary>
public static class CacheServiceExtensions
{
    /// <summary>
    /// 添加Redis缓存服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <param name="configurationSection">Redis配置节名称，默认为"Redis"</param>
    /// <param name="enableLocalCache">是否启用本地内存缓存，默认为true</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSection = "Redis",
        bool enableLocalCache = true)
    {
        // 添加本地内存缓存（如果启用）
        if (enableLocalCache)
        {
            services.AddMemoryCache();
        }

        // 获取Redis连接字符串
        var connectionString = configuration.GetConnectionString(configurationSection)
                               ?? configuration.GetSection(configurationSection)["ConnectionString"]
                               ?? "localhost:6379";

        // 注册Redis连接
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var configurationOptions = ConfigurationOptions.Parse(connectionString);

            // 可以在这里添加更多Redis配置
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectRetry = 3;
            configurationOptions.ConnectTimeout = 5000;
            configurationOptions.SyncTimeout = 5000;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        // 注册Redis数据库
        services.AddScoped<IDatabase>(provider =>
        {
            var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            return multiplexer.GetDatabase();
        });

        return services;
    }

    /// <summary>
    /// 添加Redis缓存服务（使用连接字符串）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">Redis连接字符串</param>
    /// <param name="enableLocalCache">是否启用本地内存缓存，默认为true</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        string connectionString,
        bool enableLocalCache = true)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Redis连接字符串不能为空", nameof(connectionString));
        }

        // 添加本地内存缓存（如果启用）
        if (enableLocalCache)
        {
            services.AddMemoryCache();
        }

        // 注册Redis连接
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var configurationOptions = ConfigurationOptions.Parse(connectionString);

            // 可以在这里添加更多Redis配置
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectRetry = 3;
            configurationOptions.ConnectTimeout = 5000;
            configurationOptions.SyncTimeout = 5000;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        // 注册Redis数据库
        services.AddScoped<IDatabase>(provider =>
        {
            var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            return multiplexer.GetDatabase();
        });

        return services;
    }

    /// <summary>
    /// 添加Redis缓存服务（使用配置选项）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项委托</param>
    /// <param name="enableLocalCache">是否启用本地内存缓存，默认为true</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        Action<ConfigurationOptions> configureOptions,
        bool enableLocalCache = true)
    {
        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        // 添加本地内存缓存（如果启用）
        if (enableLocalCache)
        {
            services.AddMemoryCache();
        }

        // 注册Redis连接
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var configurationOptions = new ConfigurationOptions();
            configureOptions(configurationOptions);

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        // 注册Redis数据库
        services.AddScoped<IDatabase>(provider =>
        {
            var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            return multiplexer.GetDatabase();
        });

        return services;
    }
}