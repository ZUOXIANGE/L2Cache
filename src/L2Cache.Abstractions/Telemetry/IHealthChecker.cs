namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 健康检查器接口
/// </summary>
public interface IHealthChecker : IDisposable
{
    /// <summary>
    /// 是否正在监控
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// 检查间隔
    /// </summary>
    TimeSpan CheckInterval { get; }

    /// <summary>
    /// 健康状态变化事件
    /// </summary>
    event EventHandler<HealthStatusChangedEventArgs> HealthStatusChanged;

    /// <summary>
    /// 开始健康监控
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止健康监控
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前健康状态
    /// </summary>
    /// <returns>健康状态</returns>
    HealthStatus GetCurrentStatus();

    /// <summary>
    /// 获取健康检查历史
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <returns>健康检查历史</returns>
    IEnumerable<HealthCheckResult> GetHealthHistory(int count = 10);

    /// <summary>
    /// 添加健康检查项
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="checker">检查器</param>
    void AddHealthCheck(string name, Func<CancellationToken, Task<HealthCheckItemResult>> checker);

    /// <summary>
    /// 移除健康检查项
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <returns>是否成功移除</returns>
    bool RemoveHealthCheck(string name);

    /// <summary>
    /// 清空健康检查项
    /// </summary>
    void ClearHealthChecks();

    /// <summary>
    /// 获取所有检查项名称
    /// </summary>
    /// <returns>检查项名称列表</returns>
    IEnumerable<string> GetHealthCheckNames();
}

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

/// <summary>
/// 健康检查结果
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="status">健康状态</param>
    /// <param name="description">描述</param>
    public HealthCheckResult(HealthStatus status, string? description = null)
    {
        Status = status;
        Description = description;
        Timestamp = DateTimeOffset.UtcNow;
        Items = new Dictionary<string, HealthCheckItemResult>();
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
    /// 检查项结果
    /// </summary>
    public Dictionary<string, HealthCheckItemResult> Items { get; set; }

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

/// <summary>
/// 健康检查器选项
/// </summary>
public class HealthCheckerOptions
{
    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 检查超时时间
    /// </summary>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 失败阈值
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 成功阈值
    /// </summary>
    public int SuccessThreshold { get; set; } = 2;

    /// <summary>
    /// 历史记录保留数量
    /// </summary>
    public int HistoryRetentionCount { get; set; } = 100;

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// 是否在状态变化时发送通知
    /// </summary>
    public bool NotifyOnStatusChange { get; set; } = true;

    /// <summary>
    /// 是否在启动时立即检查
    /// </summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>
    /// 并行检查
    /// </summary>
    public bool ParallelChecks { get; set; } = true;

    /// <summary>
    /// 最大并发检查数
    /// </summary>
    public int MaxConcurrentChecks { get; set; } = 10;

    /// <summary>
    /// 降级状态阈值（错误率）
    /// </summary>
    public double DegradedThreshold { get; set; } = 0.1; // 10%

    /// <summary>
    /// 不健康状态阈值（错误率）
    /// </summary>
    public double UnhealthyThreshold { get; set; } = 0.5; // 50%
}

/// <summary>
/// 预定义健康检查项
/// </summary>
public static class PredefinedHealthChecks
{
    /// <summary>
    /// Redis连接检查
    /// </summary>
    public const string RedisConnection = "redis_connection";

    /// <summary>
    /// 本地缓存检查
    /// </summary>
    public const string LocalCache = "local_cache";

    /// <summary>
    /// 内存使用检查
    /// </summary>
    public const string MemoryUsage = "memory_usage";

    /// <summary>
    /// 响应时间检查
    /// </summary>
    public const string ResponseTime = "response_time";

    /// <summary>
    /// 错误率检查
    /// </summary>
    public const string ErrorRate = "error_rate";

    /// <summary>
    /// 命中率检查
    /// </summary>
    public const string HitRate = "hit_rate";

    /// <summary>
    /// 连接池检查
    /// </summary>
    public const string ConnectionPool = "connection_pool";

    /// <summary>
    /// 序列化检查
    /// </summary>
    public const string Serialization = "serialization";

    /// <summary>
    /// 配置检查
    /// </summary>
    public const string Configuration = "configuration";

    /// <summary>
    /// 依赖服务检查
    /// </summary>
    public const string Dependencies = "dependencies";
}