using System.Diagnostics;

namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 遥测提供程序接口
/// </summary>
public interface ITelemetryProvider : IDisposable
{
    /// <summary>
    /// 活动源名称
    /// </summary>
    string ActivitySourceName { get; }

    /// <summary>
    /// 是否启用
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 开始活动
    /// </summary>
    /// <param name="name">活动名称</param>
    /// <param name="kind">活动类型</param>
    /// <param name="parentContext">父上下文</param>
    /// <param name="tags">标签</param>
    /// <returns>活动</returns>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, 
        ActivityContext parentContext = default, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录事件
    /// </summary>
    /// <param name="name">事件名称</param>
    /// <param name="tags">标签</param>
    void RecordEvent(string name, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录异常
    /// </summary>
    /// <param name="exception">异常</param>
    /// <param name="tags">标签</param>
    void RecordException(Exception exception, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 增加计数器
    /// </summary>
    /// <param name="name">计数器名称</param>
    /// <param name="value">值</param>
    /// <param name="tags">标签</param>
    void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录直方图
    /// </summary>
    /// <param name="name">直方图名称</param>
    /// <param name="value">值</param>
    /// <param name="tags">标签</param>
    void RecordHistogram(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 设置仪表
    /// </summary>
    /// <param name="name">仪表名称</param>
    /// <param name="value">值</param>
    /// <param name="tags">标签</param>
    void SetGauge(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录缓存操作
    /// </summary>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="operation">操作类型</param>
    /// <param name="key">缓存键</param>
    /// <param name="level">缓存级别</param>
    /// <param name="hit">是否命中</param>
    /// <param name="duration">持续时间</param>
    /// <param name="size">数据大小</param>
    /// <param name="tags">额外标签</param>
    void RecordCacheOperation(string cacheName, CacheOperation operation, string key, 
        CacheLevel? level = null, bool? hit = null, TimeSpan? duration = null, 
        long? size = null, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录批量操作
    /// </summary>
    void RecordBatchOperation(string cacheName, string operation, int keyCount, TimeSpan responseTime, int successCount);

    /// <summary>
    /// 记录缓存错误
    /// </summary>
    void RecordCacheError(string cacheName, string operation, Exception error, TimeSpan responseTime);

    /// <summary>
    /// 记录数据源加载
    /// </summary>
    void RecordDataSourceLoad(string cacheName, string key, TimeSpan responseTime, bool success);

    /// <summary>
    /// 记录缓存性能指标
    /// </summary>
    /// <param name="metrics">性能指标</param>
    void RecordCacheMetrics(CachePerformanceMetrics metrics);

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    /// <param name="cacheName">缓存名称</param>
    /// <returns>统计信息</returns>
    CacheStatistics? GetCacheStatistics(string cacheName);

    /// <summary>
    /// 获取所有缓存的统计信息
    /// </summary>
    /// <returns>缓存名称到统计信息的字典</returns>
    Dictionary<string, CacheStatistics> GetAllCacheStatistics();

    /// <summary>
    /// 重置指定缓存的统计信息
    /// </summary>
    /// <param name="cacheName">缓存名称</param>
    void ResetStatistics(string cacheName);

    /// <summary>
    /// 重置所有缓存的统计信息
    /// </summary>
    void ResetAllStatistics();

    /// <summary>
    /// 创建计时器
    /// </summary>
    /// <param name="name">计时器名称</param>
    /// <param name="tags">标签</param>
    /// <returns>计时器</returns>
    IDisposable CreateTimer(string name, IEnumerable<KeyValuePair<string, object>>? tags = null);
}

/// <summary>
/// 缓存操作类型
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// 无操作
    /// </summary>
    None,

    /// <summary>
    /// 获取
    /// </summary>
    Get,

    /// <summary>
    /// 设置
    /// </summary>
    Set,

    /// <summary>
    /// 删除
    /// </summary>
    Delete,

    /// <summary>
    /// 移除
    /// </summary>
    Remove,

    /// <summary>
    /// 清空
    /// </summary>
    Clear,

    /// <summary>
    /// 存在检查
    /// </summary>
    Exists,

    /// <summary>
    /// 批量获取
    /// </summary>
    BatchGet,

    /// <summary>
    /// 批量设置
    /// </summary>
    BatchSet,

    /// <summary>
    /// 批量删除
    /// </summary>
    BatchDelete,

    /// <summary>
    /// 重新加载
    /// </summary>
    Reload,

    /// <summary>
    /// 驱逐
    /// </summary>
    Evict
}

/// <summary>
/// 缓存性能指标
/// </summary>
public class CachePerformanceMetrics
{
    /// <summary>
    /// 缓存名称
    /// </summary>
    public string CacheName { get; set; } = string.Empty;

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 命中数
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// 命中数 (别名)
    /// </summary>
    public long HitCount => Hits;

    /// <summary>
    /// 未命中数
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// 未命中数 (别名)
    /// </summary>
    public long MissCount => Misses;

    /// <summary>
    /// 设置数
    /// </summary>
    public long SetCount { get; set; }

    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)Hits / TotalRequests : 0;

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 平均延迟 (别名)
    /// </summary>
    public TimeSpan AverageLatency => TimeSpan.FromMilliseconds(AverageResponseTime);

    /// <summary>
    /// 最大响应时间（毫秒）
    /// </summary>
    public double MaxResponseTime { get; set; }

    /// <summary>
    /// 最小响应时间（毫秒）
    /// </summary>
    public double MinResponseTime { get; set; }

    /// <summary>
    /// 错误数
    /// </summary>
    public long Errors { get; set; }

    /// <summary>
    /// 错误数 (别名)
    /// </summary>
    public long ErrorCount => Errors;

    /// <summary>
    /// 超时数
    /// </summary>
    public long Timeouts { get; set; }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int CurrentConnections { get; set; }

    /// <summary>
    /// 内存使用量（字节）
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// 缓存项数量
    /// </summary>
    public long ItemCount { get; set; }

    /// <summary>
    /// 驱逐数
    /// </summary>
    public long Evictions { get; set; }

    /// <summary>
    /// 驱逐数 (别名)
    /// </summary>
    public long EvictionCount => Evictions;

    /// <summary>
    /// 过期数
    /// </summary>
    public long Expirations { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}


/// <summary>
/// 遥测常量
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// 活动名称
    /// </summary>
    public static class ActivityNames
    {
        public const string CacheGet = "cache.get";
        public const string CacheSet = "cache.set";
        public const string CacheDelete = "cache.delete";
        public const string CacheClear = "cache.clear";
        public const string CacheExists = "cache.exists";
        public const string CacheBatchGet = "cache.batch_get";
        public const string CacheBatchSet = "cache.batch_set";
        public const string CacheBatchDelete = "cache.batch_delete";
        public const string CacheReload = "cache.reload";
        public const string CacheEvict = "cache.evict";
        public const string CacheGetOrLoad = "cache.get_or_load";
        public const string CachePutIfAbsent = "cache.put_if_absent";
        public const string CacheBatchGetOrLoad = "cache.batch_get_or_load";
    }

    /// <summary>
    /// 指标名称
    /// </summary>
    public static class MetricNames
    {
        public const string CacheRequests = "cache_requests_total";
        public const string CacheHits = "cache_hits_total";
        public const string CacheMisses = "cache_misses_total";
        public const string CacheErrors = "cache_errors_total";
        public const string CacheTimeouts = "cache_timeouts_total";
        public const string CacheResponseTime = "cache_response_time_seconds";
        public const string CacheSize = "cache_size_bytes";
        public const string CacheItemCount = "cache_item_count";
        public const string CacheEvictions = "cache_evictions_total";
        public const string CacheExpirations = "cache_expirations_total";
        public const string CacheConnections = "cache_connections";
        public const string CacheMemoryUsage = "cache_memory_usage_bytes";
    }

    /// <summary>
    /// 标签名称
    /// </summary>
    public static class TagNames
    {
        public const string CacheName = "cache_name";
        public const string CacheType = "cache_type";
        public const string Operation = "operation";
        public const string Result = "result";
        public const string ErrorType = "error_type";
        public const string KeyPattern = "key_pattern";
        public const string ValueType = "value_type";
        public const string Source = "source";
    }

    /// <summary>
    /// 标签值
    /// </summary>
    public static class TagValues
    {
        public const string Hit = "hit";
        public const string Miss = "miss";
        public const string Error = "error";
        public const string Success = "success";
        public const string Timeout = "timeout";
        public const string Local = "local";
        public const string Redis = "redis";
        public const string Both = "both";
    }
}