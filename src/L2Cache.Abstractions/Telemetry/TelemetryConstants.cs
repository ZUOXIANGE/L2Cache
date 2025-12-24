namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 遥测常量定义。
/// <para>
/// 集中管理用于 OpenTelemetry 和日志记录的 Activity 名称、Metric 名称和 Tag 键值。
/// 确保整个库中使用一致的遥测标识符。
/// </para>
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// 分布式追踪 (Tracing) 的活动名称。
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
        public const string CacheBatchEvict = "cache.batch_evict";
    }

    /// <summary>
    /// 监控指标 (Metrics) 名称。
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
    /// 遥测标签 (Tags/Attributes) 名称。
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
    /// 常用标签值 (Tag Values)。
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
