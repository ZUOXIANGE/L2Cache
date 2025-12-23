namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 缓存统计信息
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// 缓存名称
    /// </summary>
    public string CacheName { get; set; } = string.Empty;

    /// <summary>
    /// L1缓存命中次数
    /// </summary>
    public long L1HitCount { get; set; }

    /// <summary>
    /// L1缓存未命中次数
    /// </summary>
    public long L1MissCount { get; set; }

    /// <summary>
    /// L2缓存命中次数
    /// </summary>
    public long L2HitCount { get; set; }

    /// <summary>
    /// L2缓存未命中次数
    /// </summary>
    public long L2MissCount { get; set; }

    /// <summary>
    /// 设置次数
    /// </summary>
    public long SetCount { get; set; }

    /// <summary>
    /// 驱逐次数
    /// </summary>
    public long EvictCount { get; set; }

    /// <summary>
    /// 错误次数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 数据源加载次数
    /// </summary>
    public long DataSourceLoadCount { get; set; }

    /// <summary>
    /// 数据源加载成功次数
    /// </summary>
    public long DataSourceLoadSuccessCount { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// 最大响应时间（毫秒）
    /// </summary>
    public double MaxResponseTimeMs { get; set; }

    /// <summary>
    /// 最小响应时间（毫秒）
    /// </summary>
    public double MinResponseTimeMs { get; set; }

    /// <summary>
    /// 统计开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; }

    /// <summary>
    /// 总命中次数
    /// </summary>
    public long HitCount => L1HitCount + L2HitCount;

    /// <summary>
    /// 总未命中次数
    /// </summary>
    public long MissCount => L1MissCount + L2MissCount;

    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRate => (HitCount + MissCount) > 0 
        ? (double)HitCount / (HitCount + MissCount) 
        : 0;

    /// <summary>
    /// 命中率 (Alias for HitRate)
    /// </summary>
    public double HitRatio => HitRate;

    /// <summary>
    /// 总操作次数
    /// </summary>
    public long TotalOperations => HitCount + MissCount + SetCount + EvictCount;
}