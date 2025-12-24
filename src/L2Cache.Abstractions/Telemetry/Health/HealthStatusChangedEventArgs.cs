namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 健康状态变化事件参数
/// </summary>
public class HealthStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="previousStatus">之前的状态</param>
    /// <param name="currentStatus">当前状态</param>
    /// <param name="result">检查结果</param>
    public HealthStatusChangedEventArgs(HealthStatus previousStatus, HealthStatus currentStatus, HealthCheckResult result)
    {
        PreviousStatus = previousStatus;
        CurrentStatus = currentStatus;
        Result = result;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 之前的状态
    /// </summary>
    public HealthStatus PreviousStatus { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public HealthStatus CurrentStatus { get; }

    /// <summary>
    /// 检查结果
    /// </summary>
    public HealthCheckResult Result { get; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 状态是否改善
    /// </summary>
    public bool IsImprovement => GetStatusPriority(CurrentStatus) > GetStatusPriority(PreviousStatus);

    /// <summary>
    /// 状态是否恶化
    /// </summary>
    public bool IsDegradation => GetStatusPriority(CurrentStatus) < GetStatusPriority(PreviousStatus);

    /// <summary>
    /// 获取状态优先级
    /// </summary>
    /// <param name="status">状态</param>
    /// <returns>优先级</returns>
    private static int GetStatusPriority(HealthStatus status)
    {
        return status switch
        {
            HealthStatus.Healthy => 3,
            HealthStatus.Degraded => 2,
            HealthStatus.Unhealthy => 1,
            HealthStatus.Unknown => 0,
            _ => 0
        };
    }
}
