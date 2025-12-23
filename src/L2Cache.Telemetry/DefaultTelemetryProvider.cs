using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using L2Cache.Abstractions.Telemetry;

namespace L2Cache.Telemetry;

/// <summary>
/// 默认遥测提供程序实现
/// </summary>
public class DefaultTelemetryProvider : ITelemetryProvider
{
    private readonly ILogger<DefaultTelemetryProvider> _logger;
    private readonly TelemetryOptions _options;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, CacheStatisticsInternal> _statistics = new();

    // 指标
    private readonly Counter<long> _requestsCounter;
    private readonly Counter<long> _hitsCounter;
    private readonly Counter<long> _missesCounter;
    private readonly Counter<long> _errorsCounter;
    private readonly Counter<long> _timeoutsCounter;
    private readonly Histogram<double> _responseTimeHistogram;
    private readonly Histogram<long> _cacheSizeHistogram;
    private readonly UpDownCounter<long> _itemCountGauge;
    private readonly Counter<long> _evictionsCounter;
    private readonly Counter<long> _expirationsCounter;
    private readonly UpDownCounter<int> _connectionsGauge;
    private readonly UpDownCounter<long> _memoryUsageGauge;

    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">遥测选项</param>
    /// <param name="logger">日志记录器</param>
    public DefaultTelemetryProvider(TelemetryOptions options, ILogger<DefaultTelemetryProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 创建活动源
        _activitySource = new ActivitySource(_options.ActivitySourceName, _options.ActivitySourceVersion);

        // 创建指标
        _meter = new Meter(_options.ActivitySourceName, _options.ActivitySourceVersion);

        // 初始化计数器和直方图
        _requestsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheRequests}",
            "requests", "缓存请求总数");

        _hitsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheHits}",
            "hits", "缓存命中总数");

        _missesCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheMisses}",
            "misses", "缓存未命中总数");

        _errorsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheErrors}",
            "errors", "缓存错误总数");

        _timeoutsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheTimeouts}",
            "timeouts", "缓存超时总数");

        _responseTimeHistogram = _meter.CreateHistogram<double>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheResponseTime}",
            "seconds", "缓存响应时间");

        _cacheSizeHistogram = _meter.CreateHistogram<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheSize}",
            "bytes", "缓存大小");

        _itemCountGauge = _meter.CreateUpDownCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheItemCount}",
            "items", "缓存项数量");

        _evictionsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheEvictions}",
            "evictions", "缓存驱逐总数");

        _expirationsCounter = _meter.CreateCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheExpirations}",
            "expirations", "缓存过期总数");

        _connectionsGauge = _meter.CreateUpDownCounter<int>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheConnections}",
            "connections", "缓存连接数");

        _memoryUsageGauge = _meter.CreateUpDownCounter<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheMemoryUsage}",
            "bytes", "缓存内存使用量");

        _logger.LogInformation("遥测提供程序已初始化，活动源: {ActivitySource}, 指标前缀: {MetricsPrefix}",
            _options.ActivitySourceName, _options.MetricsPrefix);
    }

    /// <inheritdoc />
    public string ActivitySourceName => _options.ActivitySourceName;

    /// <inheritdoc />
    public bool IsEnabled => _options.EnableTelemetry;

    /// <inheritdoc />
    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal,
        ActivityContext parentContext = default, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!_options.EnableTracing || !IsEnabled)
            return null;

        try
        {
            var activity = parentContext == default
                ? _activitySource.StartActivity(name, kind)
                : _activitySource.StartActivity(name, kind, parentContext);

            if (activity != null)
            {
                // 添加自定义标签
                AddCustomTags(activity);

                // 添加传入的标签
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        activity.SetTag(tag.Key, tag.Value?.ToString());
                    }
                }

                // 应用采样
                if (_options.SamplingRatio < 1.0 && Random.Shared.NextDouble() > _options.SamplingRatio)
                {
                    activity.ActivityTraceFlags = ActivityTraceFlags.None;
                }
            }

            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动活动时发生异常: {ActivityName}", name);
            return null;
        }
    }

    /// <inheritdoc />
    public void RecordEvent(string name, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!IsEnabled)
            return;

        try
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                var eventTags = new ActivityTagsCollection();
                    
                // 添加自定义标签
                AddCustomTagsToCollection(eventTags);
                    
                // 添加传入的标签
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        eventTags[tag.Key] = tag.Value;
                    }
                }

                activity.AddEvent(new ActivityEvent(name, DateTimeOffset.UtcNow, eventTags));
            }

            _logger.LogDebug("记录事件: {EventName}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录事件时发生异常: {EventName}", name);
        }
    }

    /// <inheritdoc />
    public void RecordException(Exception exception, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!IsEnabled || exception == null)
            return;

        try
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                var eventTags = new ActivityTagsCollection
                {
                    ["exception.type"] = exception.GetType().FullName,
                    ["exception.message"] = exception.Message,
                    ["exception.stacktrace"] = exception.StackTrace
                };

                // 添加自定义标签
                AddCustomTagsToCollection(eventTags);

                // 添加传入的标签
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        eventTags[tag.Key] = tag.Value;
                    }
                }

                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, eventTags));
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            }

            _logger.LogDebug("记录异常: {ExceptionType}", exception.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录异常时发生异常");
        }
    }

    /// <inheritdoc />
    public void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!_options.EnableMetrics || !IsEnabled)
            return;

        try
        {
            var tagList = CreateTagList(tags);
                
            // 这里可以根据名称选择合适的计数器
            // 为了简化，我们使用通用的请求计数器
            _requestsCounter.Add(value, tagList);

            _logger.LogDebug("增加计数器: {CounterName}, 值: {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "增加计数器时发生异常: {CounterName}", name);
        }
    }

    /// <inheritdoc />
    public void RecordHistogram(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!_options.EnableMetrics || !IsEnabled)
            return;

        try
        {
            var tagList = CreateTagList(tags);
            _responseTimeHistogram.Record(value, tagList);

            _logger.LogDebug("记录直方图: {HistogramName}, 值: {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录直方图时发生异常: {HistogramName}", name);
        }
    }

    /// <inheritdoc />
    public void SetGauge(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        if (!_options.EnableMetrics || !IsEnabled)
            return;

        try
        {
            var tagList = CreateTagList(tags);
                
            // 根据名称选择合适的仪表
            if (name.Contains("item_count"))
            {
                _itemCountGauge.Add((long)value, tagList);
            }
            else if (name.Contains("connections"))
            {
                _connectionsGauge.Add((int)value, tagList);
            }
            else if (name.Contains("memory"))
            {
                _memoryUsageGauge.Add((long)value, tagList);
            }

            _logger.LogDebug("设置仪表: {GaugeName}, 值: {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置仪表时发生异常: {GaugeName}", name);
        }
    }

    /// <inheritdoc />
    public void RecordCacheOperation(string cacheName, CacheOperation operation, string key,
        CacheLevel? level = null, bool? hit = null, TimeSpan? duration = null,
        long? size = null, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        // 1. 更新内存统计信息
        if (!string.IsNullOrEmpty(cacheName))
        {
            var stats = GetOrCreateStatistics(cacheName);
            lock (stats.LockObject)
            {
                if (operation == CacheOperation.Get || operation == CacheOperation.BatchGet)
                {
                    if (hit.HasValue)
                    {
                        if (hit.Value)
                        {
                            if (level == CacheLevel.L1) stats.L1HitCount++;
                            else stats.L2HitCount++; // 默认为L2或Both都算作L2命中，或者根据需要调整
                        }
                        else
                        {
                            if (level == CacheLevel.L1) stats.L1MissCount++;
                            else stats.L2MissCount++;
                        }
                    }
                }
                else if (operation == CacheOperation.Set || operation == CacheOperation.BatchSet)
                {
                    stats.SetCount++;
                    if (size.HasValue) stats.TotalDataSize += size.Value;
                }
                else if (operation == CacheOperation.Evict)
                {
                    stats.EvictCount++;
                }

                if (duration.HasValue)
                {
                    UpdateResponseTime(stats, duration.Value);
                }
                stats.LastUpdateTime = DateTime.UtcNow;
            }
        }

        // 2. 遥测记录
        if (!IsEnabled)
            return;

        try
        {
            var operationTags = new List<KeyValuePair<string, object>>
            {
                new(TelemetryConstants.TagNames.Operation, operation.ToString().ToLowerInvariant())
            };

            if (!string.IsNullOrEmpty(cacheName))
            {
                operationTags.Add(new(TelemetryConstants.TagNames.CacheName, cacheName));
            }

            if (level.HasValue)
            {
                operationTags.Add(new(TelemetryConstants.TagNames.CacheType, level.Value.ToString()));
            }

            // 添加缓存键（如果启用且不超过最大长度）
            if (_options.RecordCacheKeys && !string.IsNullOrEmpty(key))
            {
                var recordedKey = key.Length > _options.MaxKeyLength 
                    ? key.Substring(0, _options.MaxKeyLength) + "..." 
                    : key;
                operationTags.Add(new(TelemetryConstants.TagNames.KeyPattern, recordedKey));
            }

            // 添加结果标签
            if (hit.HasValue)
            {
                operationTags.Add(new(TelemetryConstants.TagNames.Result, 
                    hit.Value ? TelemetryConstants.TagValues.Hit : TelemetryConstants.TagValues.Miss));
            }

            // 添加传入的标签
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    operationTags.Add(tag);
                }
            }

            var tagList = CreateTagList(operationTags);

            // 记录请求计数
            _requestsCounter.Add(1, tagList);

            // 记录命中/未命中
            if (hit.HasValue)
            {
                if (hit.Value)
                {
                    _hitsCounter.Add(1, tagList);
                }
                else
                {
                    _missesCounter.Add(1, tagList);
                }
            }

            // 记录响应时间
            if (duration.HasValue)
            {
                _responseTimeHistogram.Record(duration.Value.TotalSeconds, tagList);
            }

            // 记录大小
            if (size.HasValue && _options.RecordCacheValueSize)
            {
                _cacheSizeHistogram.Record(size.Value, tagList);
            }

            _logger.LogDebug("记录缓存操作: {CacheName}, {Operation}, 键: {Key}, 命中: {Hit}", cacheName, operation, key, hit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录缓存操作时发生异常: {Operation}", operation);
        }
    }

    /// <inheritdoc />
    public void RecordBatchOperation(string cacheName, string operation, int keyCount, TimeSpan responseTime, int successCount)
    {
         if (!string.IsNullOrEmpty(cacheName))
        {
            var stats = GetOrCreateStatistics(cacheName);
            lock (stats.LockObject)
            {
                stats.BatchOperationCount++;
                stats.BatchKeyCount += keyCount;
                stats.BatchSuccessCount += successCount;
                UpdateResponseTime(stats, responseTime);
                stats.LastUpdateTime = DateTime.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public void RecordCacheError(string cacheName, string operation, Exception error, TimeSpan responseTime)
    {
         if (!string.IsNullOrEmpty(cacheName))
        {
            var stats = GetOrCreateStatistics(cacheName);
            lock (stats.LockObject)
            {
                stats.ErrorCount++;
                stats.LastError = error.Message;
                stats.LastErrorTime = DateTime.UtcNow;
                stats.LastUpdateTime = DateTime.UtcNow;
            }
        }
        
        RecordException(error, new Dictionary<string, object>
        {
            { TelemetryConstants.TagNames.CacheName, cacheName },
            { TelemetryConstants.TagNames.Operation, operation }
        });
    }

    /// <inheritdoc />
    public void RecordDataSourceLoad(string cacheName, string key, TimeSpan responseTime, bool success)
    {
         if (!string.IsNullOrEmpty(cacheName))
        {
            var stats = GetOrCreateStatistics(cacheName);
            lock (stats.LockObject)
            {
                stats.DataSourceLoadCount++;
                if (success) stats.DataSourceLoadSuccessCount++;
                UpdateResponseTime(stats, responseTime);
                stats.LastUpdateTime = DateTime.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public CacheStatistics? GetCacheStatistics(string cacheName)
    {
        if (_statistics.TryGetValue(cacheName, out var internalStats))
        {
            lock (internalStats.LockObject)
            {
                return ConvertToPublicStatistics(internalStats);
            }
        }
        return new CacheStatistics { CacheName = cacheName };
    }

    /// <inheritdoc />
    public Dictionary<string, CacheStatistics> GetAllCacheStatistics()
    {
        var result = new Dictionary<string, CacheStatistics>();
        foreach (var kvp in _statistics)
        {
            lock (kvp.Value.LockObject)
            {
                result[kvp.Key] = ConvertToPublicStatistics(kvp.Value);
            }
        }
        return result;
    }

    /// <inheritdoc />
    public void ResetStatistics(string cacheName)
    {
        if (_statistics.TryGetValue(cacheName, out var stats))
        {
            lock (stats.LockObject)
            {
                ResetInternalStatistics(stats);
            }
            _logger.LogInformation("Cache statistics reset for: {CacheName}", cacheName);
        }
    }

    /// <inheritdoc />
    public void ResetAllStatistics()
    {
        foreach (var kvp in _statistics)
        {
            lock (kvp.Value.LockObject)
            {
                ResetInternalStatistics(kvp.Value);
            }
        }
        _logger.LogInformation("All cache statistics reset");
    }

    /// <inheritdoc />
    public void RecordCacheMetrics(CachePerformanceMetrics metrics)
    {
        // Implementation for interface compliance
    }

    /// <inheritdoc />
    public IDisposable CreateTimer(string name, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        return new TelemetryTimer(this, name, tags);
    }

    private class TelemetryTimer : IDisposable
    {
        private readonly DefaultTelemetryProvider _provider;
        private readonly string _name;
        private readonly IEnumerable<KeyValuePair<string, object>>? _tags;
        private readonly long _startTimestamp;

        public TelemetryTimer(DefaultTelemetryProvider provider, string name, IEnumerable<KeyValuePair<string, object>>? tags)
        {
            _provider = provider;
            _name = name;
            _tags = tags;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
            _provider.RecordHistogram(_name, elapsed.TotalSeconds, _tags);
        }
    }

    private CacheStatisticsInternal GetOrCreateStatistics(string cacheName)
    {
        return _statistics.GetOrAdd(cacheName, name => new CacheStatisticsInternal
        {
            CacheName = name,
            StartTime = DateTime.UtcNow,
            LastUpdateTime = DateTime.UtcNow,
            MinResponseTimeMs = double.MaxValue
        });
    }

    private void UpdateResponseTime(CacheStatisticsInternal stats, TimeSpan responseTime)
    {
        var responseTimeMs = responseTime.TotalMilliseconds;
        stats.TotalResponseTimeMs += responseTimeMs;
        stats.ResponseTimeCount++;
        if (responseTimeMs > stats.MaxResponseTimeMs) stats.MaxResponseTimeMs = responseTimeMs;
        if (responseTimeMs < stats.MinResponseTimeMs) stats.MinResponseTimeMs = responseTimeMs;
    }

    private CacheStatistics ConvertToPublicStatistics(CacheStatisticsInternal internalStats)
    {
        return new CacheStatistics
        {
            CacheName = internalStats.CacheName,
            L1HitCount = internalStats.L1HitCount,
            L1MissCount = internalStats.L1MissCount,
            L2HitCount = internalStats.L2HitCount,
            L2MissCount = internalStats.L2MissCount,
            SetCount = internalStats.SetCount,
            EvictCount = internalStats.EvictCount,
            ErrorCount = internalStats.ErrorCount,
            DataSourceLoadCount = internalStats.DataSourceLoadCount,
            DataSourceLoadSuccessCount = internalStats.DataSourceLoadSuccessCount,
            AverageResponseTimeMs = internalStats.ResponseTimeCount > 0 ? internalStats.TotalResponseTimeMs / internalStats.ResponseTimeCount : 0,
            MaxResponseTimeMs = internalStats.MaxResponseTimeMs == double.MaxValue ? 0 : internalStats.MaxResponseTimeMs,
            MinResponseTimeMs = internalStats.MinResponseTimeMs == double.MaxValue ? 0 : internalStats.MinResponseTimeMs,
            StartTime = internalStats.StartTime,
            LastUpdateTime = internalStats.LastUpdateTime
        };
    }

    private void ResetInternalStatistics(CacheStatisticsInternal stats)
    {
        stats.L1HitCount = 0;
        stats.L1MissCount = 0;
        stats.L2HitCount = 0;
        stats.L2MissCount = 0;
        stats.SetCount = 0;
        stats.EvictCount = 0;
        stats.ErrorCount = 0;
        stats.DataSourceLoadCount = 0;
        stats.DataSourceLoadSuccessCount = 0;
        stats.TotalResponseTimeMs = 0;
        stats.ResponseTimeCount = 0;
        stats.MaxResponseTimeMs = 0;
        stats.MinResponseTimeMs = double.MaxValue;
        stats.BatchOperationCount = 0;
        stats.BatchKeyCount = 0;
        stats.BatchSuccessCount = 0;
        stats.TotalDataSize = 0;
        stats.StartTime = DateTime.UtcNow;
        stats.LastUpdateTime = DateTime.UtcNow;
        stats.LastError = null;
        stats.LastErrorTime = null;
    }

    private void AddCustomTags(Activity activity)
    {
        if (_options.CustomTags != null)
        {
            foreach (var tag in _options.CustomTags)
            {
                activity.SetTag(tag.Key, tag.Value?.ToString());
            }
        }
    }

    private void AddCustomTagsToCollection(ActivityTagsCollection tags)
    {
        if (_options.CustomTags != null)
        {
            foreach (var tag in _options.CustomTags)
            {
                tags[tag.Key] = tag.Value;
            }
        }
    }

    private TagList CreateTagList(IEnumerable<KeyValuePair<string, object>>? tags)
    {
        var tagList = new TagList();
        
        if (_options.CustomTags != null)
        {
            foreach (var tag in _options.CustomTags)
            {
                tagList.Add(tag.Key, tag.Value);
            }
        }

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value);
            }
        }

        return tagList;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activitySource?.Dispose();
        _meter?.Dispose();
        
        _logger.LogInformation("遥测提供程序已释放");
    }

    private class CacheStatisticsInternal
    {
        public object LockObject { get; } = new object();
        public string CacheName { get; set; } = string.Empty;
        public long L1HitCount { get; set; }
        public long L1MissCount { get; set; }
        public long L2HitCount { get; set; }
        public long L2MissCount { get; set; }
        public long SetCount { get; set; }
        public long EvictCount { get; set; }
        public long ErrorCount { get; set; }
        public long DataSourceLoadCount { get; set; }
        public long DataSourceLoadSuccessCount { get; set; }
        public double TotalResponseTimeMs { get; set; }
        public long ResponseTimeCount { get; set; }
        public double MaxResponseTimeMs { get; set; }
        public double MinResponseTimeMs { get; set; }
        public long BatchOperationCount { get; set; }
        public long BatchKeyCount { get; set; }
        public long BatchSuccessCount { get; set; }
        public long TotalDataSize { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorTime { get; set; }
    }
}
