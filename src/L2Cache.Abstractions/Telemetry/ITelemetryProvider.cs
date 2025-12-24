using System.Diagnostics;

namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 遥测提供程序接口。
/// <para>
/// 抽象了底层的遥测实现（如 OpenTelemetry），提供统一的 API 用于记录：
/// 1. 分布式追踪 (Tracing) - StartActivity
/// 2. 监控指标 (Metrics) - IncrementCounter, RecordHistogram 等
/// 3. 结构化日志/事件 (Logging/Events) - RecordEvent
/// </para>
/// </summary>
public interface ITelemetryProvider : IDisposable
{
    /// <summary>
    /// 获取活动源 (ActivitySource) 的名称。
    /// </summary>
    string ActivitySourceName { get; }

    /// <summary>
    /// 获取当前遥测提供程序是否已启用。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 开始一个新的追踪活动 (Activity)。
    /// </summary>
    /// <param name="name">活动名称。</param>
    /// <param name="kind">活动类型（Server, Client, Producer, Consumer, Internal）。默认为 Internal。</param>
    /// <param name="parentContext">父上下文（用于串联分布式追踪）。</param>
    /// <param name="tags">初始标签集合。</param>
    /// <returns>创建的 Activity 对象，如果未采样或创建失败可能返回 null。</returns>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, 
        ActivityContext parentContext = default, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录一个瞬时事件。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="tags">事件相关的标签/属性。</param>
    void RecordEvent(string name, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录异常信息。
    /// </summary>
    /// <param name="exception">异常对象。</param>
    /// <param name="tags">异常相关的标签/属性。</param>
    void RecordException(Exception exception, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 增加计数器 (Counter) 的值。
    /// <para>适用于累加型指标，如请求总数、错误总数。</para>
    /// </summary>
    /// <param name="name">计数器名称。</param>
    /// <param name="value">增加的值，默认为 1。</param>
    /// <param name="tags">标签。</param>
    void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录直方图 (Histogram) 数据。
    /// <para>适用于统计分布情况，如响应耗时、请求大小。</para>
    /// </summary>
    /// <param name="name">直方图名称。</param>
    /// <param name="value">观测值。</param>
    /// <param name="tags">标签。</param>
    void RecordHistogram(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 设置仪表 (Gauge) 的值。
    /// <para>适用于记录当前状态的快照，如当前内存使用量、当前连接数。</para>
    /// </summary>
    /// <param name="name">仪表名称。</param>
    /// <param name="value">当前值。</param>
    /// <param name="tags">标签。</param>
    void SetGauge(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录一次完整的缓存操作详情。
    /// <para>这是一个高级便捷方法，内部可能同时记录 Metric 和 Log。</para>
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    /// <param name="operation">操作类型枚举。</param>
    /// <param name="key">缓存键。</param>
    /// <param name="level">缓存层级 (L1/L2)。</param>
    /// <param name="hit">是否命中。</param>
    /// <param name="duration">操作耗时。</param>
    /// <param name="size">数据大小（字节）。</param>
    /// <param name="tags">额外标签。</param>
    void RecordCacheOperation(string cacheName, CacheOperation operation, string key, 
        CacheLevel? level = null, bool? hit = null, TimeSpan? duration = null, 
        long? size = null, IEnumerable<KeyValuePair<string, object>>? tags = null);

    /// <summary>
    /// 记录批量操作的结果。
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    /// <param name="operation">操作名称 (如 batch_get)。</param>
    /// <param name="keyCount">处理的 Key 数量。</param>
    /// <param name="responseTime">总响应时间。</param>
    /// <param name="successCount">成功的数量。</param>
    void RecordBatchOperation(string cacheName, string operation, int keyCount, TimeSpan responseTime, int successCount);

    /// <summary>
    /// 记录缓存操作中的错误。
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    /// <param name="operation">操作名称。</param>
    /// <param name="error">异常对象。</param>
    /// <param name="responseTime">错误发生前的耗时。</param>
    void RecordCacheError(string cacheName, string operation, Exception error, TimeSpan responseTime);

    /// <summary>
    /// 记录数据源回源加载的结果。
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    /// <param name="key">缓存键。</param>
    /// <param name="responseTime">加载耗时。</param>
    /// <param name="success">是否成功加载。</param>
    void RecordDataSourceLoad(string cacheName, string key, TimeSpan responseTime, bool success);

    /// <summary>
    /// 记录一组缓存性能指标。
    /// </summary>
    /// <param name="metrics">性能指标快照对象。</param>
    void RecordCacheMetrics(CachePerformanceMetrics metrics);

    /// <summary>
    /// 获取指定缓存的统计信息。
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    /// <returns>统计信息对象，如果不存在返回 null。</returns>
    CacheStatistics? GetCacheStatistics(string cacheName);

    /// <summary>
    /// 获取所有已注册缓存的统计信息。
    /// </summary>
    /// <returns>缓存名称到统计信息的字典。</returns>
    Dictionary<string, CacheStatistics> GetAllCacheStatistics();

    /// <summary>
    /// 重置指定缓存的统计信息。
    /// </summary>
    /// <param name="cacheName">缓存名称。</param>
    void ResetStatistics(string cacheName);

    /// <summary>
    /// 重置所有缓存的统计信息。
    /// </summary>
    void ResetAllStatistics();

    /// <summary>
    /// 创建一个计时器，用于测量代码块的执行时间。
    /// <para>Dispose 时自动记录时间。</para>
    /// </summary>
    /// <param name="name">计时器名称。</param>
    /// <param name="tags">标签。</param>
    /// <returns>可释放的计时器对象。</returns>
    IDisposable CreateTimer(string name, IEnumerable<KeyValuePair<string, object>>? tags = null);
}
