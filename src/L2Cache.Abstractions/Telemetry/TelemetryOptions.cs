namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 遥测选项
/// </summary>
public class TelemetryOptions
{
    /// <summary>
    /// 是否启用遥测
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// 活动源名称
    /// </summary>
    public string ActivitySourceName { get; set; } = "L2Cache";

    /// <summary>
    /// 活动源版本
    /// </summary>
    public string ActivitySourceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// 是否启用指标
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 指标前缀
    /// </summary>
    public string MetricsPrefix { get; set; } = "l2cache";

    /// <summary>
    /// 是否启用跟踪
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// 是否启用日志
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// 采样率
    /// </summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// 是否记录缓存键
    /// </summary>
    public bool RecordCacheKeys { get; set; } = false;

    /// <summary>
    /// 是否记录缓存值大小
    /// </summary>
    public bool RecordCacheValueSize { get; set; } = true;

    /// <summary>
    /// 最大键长度
    /// </summary>
    public int MaxKeyLength { get; set; } = 100;

    /// <summary>
    /// 指标收集间隔
    /// </summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用详细指标
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = false;

    /// <summary>
    /// 自定义标签
    /// </summary>
    public Dictionary<string, object> CustomTags { get; set; } = new();

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(60);
}
