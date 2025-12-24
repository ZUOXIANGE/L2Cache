using System;

namespace L2Cache.Abstractions.Telemetry;

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
}
