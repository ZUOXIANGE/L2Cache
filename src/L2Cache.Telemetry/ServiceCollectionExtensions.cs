using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Telemetry;

namespace L2Cache.Extensions;

/// <summary>
/// L2Cache Telemetry 扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 L2Cache 遥测和健康检查支持
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealthCheck">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddL2CacheTelemetry(this IServiceCollection services, Action<HealthCheckerOptions>? configureHealthCheck = null)
    {
        // 替换默认的 NoOpTelemetryProvider 为 DefaultTelemetryProvider
        services.Replace(ServiceDescriptor.Singleton<ITelemetryProvider, DefaultTelemetryProvider>());

        // 配置健康检查选项
        var healthOptions = new HealthCheckerOptions();
        configureHealthCheck?.Invoke(healthOptions);
        services.TryAddSingleton(healthOptions);

        // 注册健康检查器
        services.TryAddSingleton<IHealthChecker, DefaultHealthChecker>();

        return services;
    }
}
