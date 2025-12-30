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
    public static void LogCacheHit(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime)
    {
        LogCacheHitInternal(logger, cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Cache hit: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogCacheHitInternal(this ILogger logger, string cacheName, string cacheLevel, string key, double responseTimeMs);

    /// <summary>
    /// 记录缓存未命中日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogCacheMiss(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime)
    {
        LogCacheMissInternal(logger, cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Cache miss: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogCacheMissInternal(this ILogger logger, string cacheName, string cacheLevel, string key, double responseTimeMs);

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
    public static void LogCacheSet(this ILogger logger, string cacheName, string cacheLevel, string key,
        TimeSpan responseTime, TimeSpan? expiry = null, long dataSize = 0)
    {
        LogCacheSetInternal(logger, cacheName, cacheLevel, key, responseTime.TotalMilliseconds, expiry?.ToString() ?? "None", dataSize);
    }

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Cache set: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTimeMs}ms, Expiry: {expiryStr}, DataSize: {dataSize}")]
    private static partial void LogCacheSetInternal(this ILogger logger, string cacheName, string cacheLevel, string key, double responseTimeMs, string expiryStr, long dataSize);

    /// <summary>
    /// 记录缓存删除日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="cacheLevel">缓存级别</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogCacheEvict(this ILogger logger, string cacheName, string cacheLevel, string key, TimeSpan responseTime)
    {
        LogCacheEvictInternal(logger, cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Cache evict: {cacheName} [{cacheLevel}] Key: {key}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogCacheEvictInternal(this ILogger logger, string cacheName, string cacheLevel, string key, double responseTimeMs);

    /// <summary>
    /// 记录缓存重新加载日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="key">缓存键</param>
    /// <param name="responseTime">响应时间</param>
    /// <param name="expiry">过期时间</param>
    public static void LogCacheReload(this ILogger logger, string cacheName, string key, TimeSpan responseTime, TimeSpan? expiry = null)
    {
        LogCacheReloadInternal(logger, cacheName, key, responseTime.TotalMilliseconds, expiry?.ToString() ?? "None");
    }

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Cache reload: {cacheName} Key: {key}, ResponseTime: {responseTimeMs}ms, Expiry: {expiryStr}")]
    private static partial void LogCacheReloadInternal(this ILogger logger, string cacheName, string key, double responseTimeMs, string expiryStr);

    /// <summary>
    /// 记录缓存清空日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogCacheClear(this ILogger logger, string cacheName, TimeSpan responseTime)
    {
        LogCacheClearInternal(logger, cacheName, responseTime.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Cache clear: {cacheName}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogCacheClearInternal(this ILogger logger, string cacheName, double responseTimeMs);

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
            LogDataSourceLoadSuccessInternal(logger, cacheName, key, responseTime.TotalMilliseconds, dataSize);
        }
        else
        {
            LogDataSourceLoadFailedInternal(logger, cacheName, key, responseTime.TotalMilliseconds);
        }
    }

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Data source load success: {cacheName} Key: {key}, ResponseTime: {responseTimeMs}ms, DataSize: {dataSize}")]
    private static partial void LogDataSourceLoadSuccessInternal(this ILogger logger, string cacheName, string key, double responseTimeMs, long dataSize);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Data source load failed: {cacheName} Key: {key}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogDataSourceLoadFailedInternal(this ILogger logger, string cacheName, string key, double responseTimeMs);

    /// <summary>
    /// 记录批量操作日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="operation">操作类型</param>
    /// <param name="keyCount">键数量</param>
    /// <param name="successCount">成功数量</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogBatchOperation(this ILogger logger, string cacheName, string operation,
        int keyCount, int successCount, TimeSpan responseTime)
    {
        LogBatchOperationInternal(logger, cacheName, operation, keyCount, successCount, responseTime.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1007, Level = LogLevel.Debug, Message = "Batch operation: {cacheName} Operation: {operation}, KeyCount: {keyCount}, SuccessCount: {successCount}, ResponseTime: {responseTimeMs}ms")]
    private static partial void LogBatchOperationInternal(this ILogger logger, string cacheName, string operation, int keyCount, int successCount, double responseTimeMs);

    /// <summary>
    /// 记录缓存错误日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="operation">操作类型</param>
    /// <param name="key">缓存键</param>
    /// <param name="exception">异常信息</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogCacheError(this ILogger logger, string cacheName, string operation, string key,
        Exception exception, TimeSpan responseTime)
    {
        LogCacheErrorInternal(logger, exception, cacheName, operation, key, responseTime.TotalMilliseconds, exception.Message);
    }

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Cache error: {cacheName} Operation: {operation}, Key: {key}, ResponseTime: {responseTimeMs}ms, Error: {errorMessage}")]
    private static partial void LogCacheErrorInternal(this ILogger logger, Exception exception, string cacheName, string operation, string key, double responseTimeMs, string errorMessage);

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
            LogCacheHealthCheckPassedInternal(logger, cacheName, responseTime.TotalMilliseconds, details ?? "OK");
        }
        else
        {
            LogCacheHealthCheckFailedInternal(logger, cacheName, responseTime.TotalMilliseconds, details ?? "Unknown error");
        }
    }

    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug, Message = "Cache health check passed: {cacheName}, ResponseTime: {responseTimeMs}ms, Details: {details}")]
    private static partial void LogCacheHealthCheckPassedInternal(this ILogger logger, string cacheName, double responseTimeMs, string details);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Cache health check failed: {cacheName}, ResponseTime: {responseTimeMs}ms, Details: {details}")]
    private static partial void LogCacheHealthCheckFailedInternal(this ILogger logger, string cacheName, double responseTimeMs, string details);
}