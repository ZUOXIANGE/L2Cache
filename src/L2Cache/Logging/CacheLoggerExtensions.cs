using Microsoft.Extensions.Logging;

namespace L2Cache.Logging;

/// <summary>
/// 缓存日志扩展方法，提供结构化的日志记录
/// </summary>
public static partial class CacheLoggerExtensions
{
    /// <summary>
    /// 记录缓存命中日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Cache hit: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTime}")]
    public static partial void LogCacheHit(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime);

    /// <summary>
    /// 记录缓存未命中日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Cache miss: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTime}")]
    public static partial void LogCacheMiss(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime);

    /// <summary>
    /// 记录缓存设置日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    /// <param name="expiry">过期时间</param>
    /// <param name="dataSize">数据大小</param>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Cache set: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTime}, Expiry: {expiry}, DataSize: {dataSize}")]
    public static partial void LogCacheSet(this ILogger logger, string cacheName, string cacheLevel, string key,
        TimeSpan responseTime, TimeSpan? expiry = null, long dataSize = 0);

    /// <summary>
    /// 记录缓存删除日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Cache evict: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTime}")]
    public static partial void LogCacheEvict(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime);

    /// <summary>
    /// 记录缓存重新加载日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    /// <param name="expiry">过期时间</param>
    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Cache reload: {cacheName} Key: {key}, ResponseTime: {responseTime}, Expiry: {expiry}")]
    public static partial void LogCacheReload(this ILogger logger, string cacheName, string key, TimeSpan responseTime, TimeSpan? expiry = null);

    /// <summary>
    /// 记录缓存清空日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Cache clear: {cacheName}, ResponseTime: {responseTime}")]
    public static partial void LogCacheClear(this ILogger logger, string cacheName, TimeSpan responseTime);

    /// <summary>
    /// 记录数据源加载日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    /// <param name="success">是否成功</param>
    /// <param name="dataSize">数据大小</param>
    public static void LogDataSourceLoad(this ILogger logger, string cacheName, string key, TimeSpan responseTime,
        bool success, long dataSize = 0)
    {
        if (success)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogDataSourceLoadSuccess(logger, cacheName, key, responseTime, dataSize);
            }
        }
        else
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogDataSourceLoadFailed(logger, cacheName, key, responseTime);
            }
        }
    }

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Data source load success: {cacheName} Key: {key}, ResponseTime: {responseTime}, DataSize: {dataSize}")]
    public static partial void LogDataSourceLoadSuccess(this ILogger logger, string cacheName, string key, TimeSpan responseTime, long dataSize);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Data source load failed: {cacheName} Key: {key}, ResponseTime: {responseTime}")]
    public static partial void LogDataSourceLoadFailed(this ILogger logger, string cacheName, string key, TimeSpan responseTime);

    /// <summary>
    /// 记录批量操作日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="operation">操作类型</param>
    /// <param name="keyCount">键数量</param>
    /// <param name="successCount">成功数量</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1007, Level = LogLevel.Debug, Message = "Batch operation: {cacheName} Operation: {operation}, KeyCount: {keyCount}, SuccessCount: {successCount}, ResponseTime: {responseTime}")]
    public static partial void LogBatchOperation(this ILogger logger, string cacheName, string operation,
        int keyCount, int successCount, TimeSpan responseTime);

    /// <summary>
    /// 记录缓存错误日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="operation">操作类型</param>
    /// <param name="key">缓存键</param>
    /// <param name="exception">异常信息</param>
    /// <param name="responseTime">响应时间</param>
    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Cache error: {cacheName} Operation: {operation}, Key: {key}, ResponseTime: {responseTime}")]
    public static partial void LogCacheError(this ILogger logger, string cacheName, string operation, string key,
        Exception exception, TimeSpan responseTime);

    /// <summary>
    /// 记录缓存健康检查日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="isHealthy">是否健康</param>
    /// <param name="responseTime">响应时间</param>
    /// <param name="details">详细信息</param>
    public static void LogCacheHealthCheck(this ILogger logger, string cacheName, bool isHealthy,
        TimeSpan responseTime, string? details = null)
    {
        if (isHealthy)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogCacheHealthCheckPassed(logger, cacheName, responseTime, details ?? "OK");
            }
        }
        else
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogCacheHealthCheckFailed(logger, cacheName, responseTime, details ?? "Unknown error");
            }
        }
    }

    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug, Message = "Cache health check passed: {cacheName}, ResponseTime: {responseTime}, Details: {details}")]
    public static partial void LogCacheHealthCheckPassed(this ILogger logger, string cacheName, TimeSpan responseTime, string details);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Cache health check failed: {cacheName}, ResponseTime: {responseTime}, Details: {details}")]
    public static partial void LogCacheHealthCheckFailed(this ILogger logger, string cacheName, TimeSpan responseTime, string details);

    /// <summary>
    /// 记录因锁争用跳过L1更新的日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="key">缓存键</param>
    [LoggerMessage(EventId = 1013, Level = LogLevel.Debug, Message = "Skipped L1 update for {cacheName} Key: {key} due to lock contention.")]
    public static partial void LogL1UpdateSkipped(this ILogger logger, string cacheName, string key);
}
