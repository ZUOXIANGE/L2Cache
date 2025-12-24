namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 健康检查项结果
/// </summary>
public class HealthCheckItemResult
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="status">健康状态</param>
    /// <param name="description">描述</param>
    public HealthCheckItemResult(HealthStatus status, string? description = null)
    {
        Status = status;
        Description = description;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// 检查耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 附加数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy => Status == HealthStatus.Healthy;

    /// <summary>
    /// 是否降级
    /// </summary>
    public bool IsDegraded => Status == HealthStatus.Degraded;

    /// <summary>
    /// 是否不健康
    /// </summary>
    public bool IsUnhealthy => Status == HealthStatus.Unhealthy;
}
