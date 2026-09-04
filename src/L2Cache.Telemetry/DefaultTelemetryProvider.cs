using System.Diagnostics;
using System.Diagnostics.Metrics;
using L2Cache.Abstractions.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

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

    // 指标（仅保留有真实数据来源的仪表）
    private readonly Counter<long> _requestsCounter;
    private readonly Counter<long> _hitsCounter;
    private readonly Counter<long> _missesCounter;
    private readonly Counter<long> _errorsCounter;
    private readonly Histogram<double> _responseTimeHistogram;
    private readonly Histogram<long> _cacheSizeHistogram;
    private readonly ObservableUpDownCounter<long>? _itemCountGauge;
    private readonly ObservableUpDownCounter<int>? _connectionsGauge;

    /// <summary>预生成的 operation 小写名称表，避免热路径重复 ToString/ToLower 分配。</summary>
    private static readonly string[] OperationNames = BuildOperationNames();

    private static string[] BuildOperationNames()
    {
        var names = Enum.GetNames<CacheOperation>();
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = names[i].ToLowerInvariant();
        }

        return names;
    }

    /// <summary>CacheLevel 的标签值（常量，避免每次装箱）。</summary>
    private static string LevelTagValue(CacheLevel level) => level switch
    {
        CacheLevel.L1 => "L1",
        CacheLevel.L2 => "L2",
        CacheLevel.Both => "Both",
        _ => level.ToString()
    };

    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">遥测选项</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="memoryCache">L1 共享内存缓存（可选，用于上报 L1 条目数仪表）。</param>
    /// <param name="connectionMultiplexer">Redis 连接（可选，用于上报连接状态仪表）。</param>
    public DefaultTelemetryProvider(
        TelemetryOptions options,
        ILogger<DefaultTelemetryProvider> logger,
        MemoryCache? memoryCache = null,
        IConnectionMultiplexer? connectionMultiplexer = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 创建活动源（名称即 OTel 订阅名；采样交由 OTel sampler，库内不做二次采样）
        _activitySource = new ActivitySource(_options.ActivitySourceName);

        // 创建指标
        _meter = new Meter(_options.ActivitySourceName);

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

        _responseTimeHistogram = _meter.CreateHistogram<double>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheResponseTime}",
            "seconds", "缓存响应时间");

        _cacheSizeHistogram = _meter.CreateHistogram<long>(
            $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheSize}",
            "bytes", "缓存大小");

        // 过程级状态仪表（Observable：由 OTel 采集端定时回调，反映绝对快照而非累加量）
        if (memoryCache != null)
        {
            _itemCountGauge = _meter.CreateObservableUpDownCounter<long>(
                $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheItemCount}",
                () => memoryCache.Count, "items", "L1 缓存条目总数");
        }

        if (connectionMultiplexer != null)
        {
            _connectionsGauge = _meter.CreateObservableUpDownCounter<int>(
                $"{_options.MetricsPrefix}_{TelemetryConstants.MetricNames.CacheConnections}",
                () => connectionMultiplexer.IsConnected ? 1 : 0, "connections", "缓存连接数（1=已连接，0=断开）");
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("遥测提供程序已初始化，活动源: {ActivitySource}, 指标前缀: {MetricsPrefix}",
                _options.ActivitySourceName, _options.MetricsPrefix);
        }
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
                // 添加传入的标签（key_pattern 遵循 RecordCacheKeys，避免默认向 Trace 泄露缓存键）
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        if (tag.Key == TelemetryConstants.TagNames.KeyPattern)
                        {
                            if (!_options.RecordCacheKeys)
                            {
                                continue;
                            }

                            var key = tag.Value?.ToString();
                            if (key is { Length: > 0 } && key.Length > _options.MaxKeyLength)
                            {
                                key = string.Concat(key.AsSpan(0, _options.MaxKeyLength), "...");
                            }

                            activity.SetTag(tag.Key, key);
                            continue;
                        }

                        activity.SetTag(tag.Key, tag.Value?.ToString());
                    }
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

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("记录事件: {EventName}", name);
            }
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

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("记录异常: {ExceptionType}", exception.GetType().Name);
            }
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

            // 按指标全名后缀路由到唯一对应的计数器；未注册的仪表忽略
            if (name.EndsWith(TelemetryConstants.MetricNames.CacheRequests, StringComparison.Ordinal))
            {
                _requestsCounter.Add(value, tagList);
            }
            else if (name.EndsWith(TelemetryConstants.MetricNames.CacheHits, StringComparison.Ordinal))
            {
                _hitsCounter.Add(value, tagList);
            }
            else if (name.EndsWith(TelemetryConstants.MetricNames.CacheMisses, StringComparison.Ordinal))
            {
                _missesCounter.Add(value, tagList);
            }
            else if (name.EndsWith(TelemetryConstants.MetricNames.CacheErrors, StringComparison.Ordinal))
            {
                _errorsCounter.Add(value, tagList);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("增加计数器: {CounterName}, 值: {Value}", name, value);
            }
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
            // 仅记录到本库持有的直方图仪表；未知名称忽略，避免串写错误语义
            if (!name.EndsWith(TelemetryConstants.MetricNames.CacheResponseTime, StringComparison.Ordinal))
            {
                return;
            }

            var tagList = CreateTagList(tags);
            _responseTimeHistogram.Record(value, tagList);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("记录直方图: {HistogramName}, 值: {Value}", name, value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录直方图时发生异常: {HistogramName}", name);
        }
    }

    /// <inheritdoc />
    public void SetGauge(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        // item_count / connections 等状态仪表已改为 ObservableUpDownCounter，
        // 由 OTel 采集端定时回调快照，外部无需（也无法增量）Set。
        if (!_options.EnableMetrics || !IsEnabled)
            return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("忽略 SetGauge 调用（状态仪表已由 Observable 驱动）: {GaugeName}", name);
        }
    }

    /// <inheritdoc />
    public void RecordCacheOperation(string cacheName, CacheOperation operation, string key,
        CacheLevel? level = null, bool? hit = null, TimeSpan? duration = null,
        long? size = null, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        // 遥测记录（指标关闭时不构建任何标签，零额外分配）
        if (!IsEnabled || !_options.EnableMetrics)
            return;

        try
        {
            var opIndex = (int)operation;
            var opName = (uint)opIndex < (uint)OperationNames.Length
                ? OperationNames[opIndex]
                : operation.ToString().ToLowerInvariant();

            var tagList = new TagList();
            tagList.Add(TelemetryConstants.TagNames.Operation, opName);
            if (!string.IsNullOrEmpty(cacheName))
            {
                tagList.Add(TelemetryConstants.TagNames.CacheName, cacheName);
            }

            if (level.HasValue)
            {
                tagList.Add(TelemetryConstants.TagNames.CacheType, LevelTagValue(level.Value));
            }

            // 添加缓存键（仅当启用记录键时）
            if (_options.RecordCacheKeys && !string.IsNullOrEmpty(key))
            {
                tagList.Add(TelemetryConstants.TagNames.KeyPattern,
                    key.Length > _options.MaxKeyLength
                        ? string.Concat(key.AsSpan(0, _options.MaxKeyLength), "...")
                        : key);
            }

            // 添加结果标签
            if (hit.HasValue)
            {
                tagList.Add(TelemetryConstants.TagNames.Result,
                    hit.Value ? TelemetryConstants.TagValues.Hit : TelemetryConstants.TagValues.Miss);
            }

            // 添加传入的额外标签
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    tagList.Add(tag.Key, tag.Value);
                }
            }

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

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("记录缓存操作: {CacheName}, {Operation}, 键: {Key}, 命中: {Hit}", cacheName, operation, key, hit);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录缓存操作时发生异常: {Operation}", operation);
        }
    }

    /// <inheritdoc />
    public void RecordBatchOperation(string cacheName, string operation, int keyCount, TimeSpan responseTime, int successCount)
    {
        // 批量操作指标：请求计数 + 耗时直方图（维度含 operation / key_count）
        if (!IsEnabled || !_options.EnableMetrics)
            return;

        try
        {
            var tagList = new TagList();
            tagList.Add(TelemetryConstants.TagNames.CacheName, cacheName);
            tagList.Add(TelemetryConstants.TagNames.Operation, operation);
            tagList.Add(TelemetryConstants.TagNames.KeyCount, keyCount);

            _requestsCounter.Add(1, tagList);
            _responseTimeHistogram.Record(responseTime.TotalSeconds, tagList);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录批量操作指标时发生异常: {Operation}", operation);
        }
    }

    /// <inheritdoc />
    public void RecordCacheError(string cacheName, string operation, Exception exception, TimeSpan responseTime)
    {
        if (IsEnabled && _options.EnableMetrics)
        {
            try
            {
                var tagList = new TagList();
                tagList.Add(TelemetryConstants.TagNames.CacheName, cacheName);
                tagList.Add(TelemetryConstants.TagNames.Operation, operation);
                tagList.Add(TelemetryConstants.TagNames.Result, TelemetryConstants.TagValues.Error);
                _errorsCounter.Add(1, tagList);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "记录缓存错误指标时发生异常: {Operation}", operation);
            }
        }

        RecordException(exception, new Dictionary<string, object>
        {
            { TelemetryConstants.TagNames.CacheName, cacheName },
            { TelemetryConstants.TagNames.Operation, operation }
        });
    }

    /// <inheritdoc />
    public void RecordDataSourceLoad(string cacheName, string key, TimeSpan responseTime, bool success)
    {
        // 回源指标：请求计数 + 耗时直方图（operation=load / source=datasource / result=success|error）
        if (!IsEnabled || !_options.EnableMetrics)
            return;

        try
        {
            var tagList = new TagList();
            tagList.Add(TelemetryConstants.TagNames.CacheName, cacheName);
            tagList.Add(TelemetryConstants.TagNames.Operation, "load");
            tagList.Add(TelemetryConstants.TagNames.Source, TelemetryConstants.TagValues.DataSource);
            tagList.Add(TelemetryConstants.TagNames.Result,
                success ? TelemetryConstants.TagValues.Success : TelemetryConstants.TagValues.Error);

            _requestsCounter.Add(1, tagList);
            _responseTimeHistogram.Record(responseTime.TotalSeconds, tagList);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录回源指标时发生异常: {CacheName}", cacheName);
        }
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

    private sealed class TelemetryTimer : IDisposable
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

    private static TagList CreateTagList(IEnumerable<KeyValuePair<string, object>>? tags)
    {
        var tagList = new TagList();

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
        GC.SuppressFinalize(this);
    }
}
