namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 健康状态
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 降级
    /// </summary>
    Degraded,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}
