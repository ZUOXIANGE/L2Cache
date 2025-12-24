namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 缓存性能指标快照。
/// <para>
/// 包含特定时间点或时间段内的缓存性能统计数据。
/// 用于监控仪表板展示、性能分析和告警。
/// </para>
/// </summary>
public class CachePerformanceMetrics
{
    /// <summary>
    /// 缓存名称/区域标识。
    /// </summary>
    public string CacheName { get; set; } = string.Empty;

    /// <summary>
    /// 总请求次数。
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 命中次数。
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// 命中次数 (Hits 的别名)。
    /// </summary>
    public long HitCount => Hits;

    /// <summary>
    /// 未命中次数。
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// 未命中次数 (Misses 的别名)。
    /// </summary>
    public long MissCount => Misses;

    /// <summary>
    /// 设置/写入次数。
    /// </summary>
    public long SetCount { get; set; }

    /// <summary>
    /// 缓存命中率 (0.0 - 1.0)。
    /// <para>计算公式: Hits / TotalRequests</para>
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)Hits / TotalRequests : 0;

    /// <summary>
    /// 平均响应时间（毫秒）。
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 平均延迟 (TimeSpan 格式)。
    /// </summary>
    public TimeSpan AverageLatency => TimeSpan.FromMilliseconds(AverageResponseTime);

    /// <summary>
    /// 最大响应时间（毫秒）。
    /// </summary>
    public double MaxResponseTime { get; set; }

    /// <summary>
    /// 最小响应时间（毫秒）。
    /// </summary>
    public double MinResponseTime { get; set; }

    /// <summary>
    /// 错误发生的总次数。
    /// </summary>
    public long Errors { get; set; }

    /// <summary>
    /// 错误次数 (Errors 的别名)。
    /// </summary>
    public long ErrorCount => Errors;

    /// <summary>
    /// 超时发生的总次数。
    /// </summary>
    public long Timeouts { get; set; }

    /// <summary>
    /// 当前活跃连接数（针对远程缓存如 Redis）。
    /// </summary>
    public int CurrentConnections { get; set; }

    /// <summary>
    /// 估算的内存使用量（字节）。
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// 当前缓存项总数量。
    /// </summary>
    public long ItemCount { get; set; }

    /// <summary>
    /// 被驱逐的缓存项数量。
    /// </summary>
    public long Evictions { get; set; }

    /// <summary>
    /// 驱逐数量 (Evictions 的别名)。
    /// </summary>
    public long EvictionCount => Evictions;

    /// <summary>
    /// 自然过期的缓存项数量。
    /// </summary>
    public long Expirations { get; set; }

    /// <summary>
    /// 指标生成的 UTC 时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
