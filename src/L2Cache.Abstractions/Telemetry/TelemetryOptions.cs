namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 遥测选项
/// </summary>
public class TelemetryOptions
{
    /// <summary>
    /// 是否启用遥测（关闭时默认提供程序零开销）
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// 活动源名称（ActivitySource / Meter 名称，OTel 订阅用此名称）
    /// </summary>
    public string ActivitySourceName { get; set; } = "L2Cache";

    /// <summary>
    /// 是否启用指标
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 指标名称前缀
    /// </summary>
    public string MetricsPrefix { get; set; } = "l2cache";

    /// <summary>
    /// 是否启用追踪（span）
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// 是否在遥测中记录缓存键（注意敏感数据）。默认 false：Trace 与 Metrics 均不落真实键
    /// </summary>
    public bool RecordCacheKeys { get; set; }

    /// <summary>
    /// 是否记录缓存值大小
    /// </summary>
    public bool RecordCacheValueSize { get; set; } = true;

    /// <summary>
    /// 记录缓存键时的最大键长度（超长截断）
    /// </summary>
    public int MaxKeyLength { get; set; } = 100;
}
