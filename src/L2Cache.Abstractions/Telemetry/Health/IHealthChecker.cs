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
