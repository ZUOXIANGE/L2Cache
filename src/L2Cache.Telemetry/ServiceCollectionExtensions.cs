using L2Cache.Abstractions.Telemetry;
using L2Cache.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Extensions;

/// <summary>
/// L2Cache Telemetry 扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 启用 L2Cache 遥测（将默认的 NoOpTelemetryProvider 替换为 DefaultTelemetryProvider）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddL2CacheTelemetry(this IServiceCollection services)
    {
        // 通过工厂注入 L1 MemoryCache 与 Redis 连接，驱动 Observable 状态仪表（条目数/连接状态）。
        services.Replace(ServiceDescriptor.Singleton<ITelemetryProvider>(sp =>
            new DefaultTelemetryProvider(
                sp.GetService<TelemetryOptions>() ?? new TelemetryOptions(),
                sp.GetRequiredService<ILogger<DefaultTelemetryProvider>>(),
                sp.GetService<IMemoryCache>() as MemoryCache,
                sp.GetService<IConnectionMultiplexer>())));

        return services;
    }
}
