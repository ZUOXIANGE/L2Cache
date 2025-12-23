using Microsoft.Extensions.Logging;

namespace L2Cache.Logging;

/// <summary>
/// 缓存日志扩展方法，提供结构化的日志记录
/// </summary>
public static class CacheLoggerExtensions
{
    // 定义日志事件ID
    private static readonly EventId CacheHitEventId = new EventId(1001, "CacheHit");
    private static readonly EventId CacheMissEventId = new EventId(1002, "CacheMiss");
    private static readonly EventId CacheSetEventId = new EventId(1003, "CacheSet");
    private static readonly EventId CacheEvictEventId = new EventId(1004, "CacheEvict");
    private static readonly EventId CacheErrorEventId = new EventId(1005, "CacheError");
    private static readonly EventId DataSourceLoadEventId = new EventId(1006, "DataSourceLoad");
    private static readonly EventId BatchOperationEventId = new EventId(1007, "BatchOperation");
    private static readonly EventId CacheReloadEventId = new EventId(1008, "CacheReload");
    private static readonly EventId CacheClearEventId = new EventId(1009, "CacheClear");

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
        logger.LogDebug(CacheHitEventId,
            "Cache hit: {CacheName} [{CacheLevel}] Key: {Key}, ResponseTime: {ResponseTime}ms",
            cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

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
        logger.LogDebug(CacheMissEventId,
            "Cache miss: {CacheName} [{CacheLevel}] Key: {Key}, ResponseTime: {ResponseTime}ms",
            cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

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
        logger.LogDebug(CacheSetEventId,
            "Cache set: {CacheName} [{CacheLevel}] Key: {Key}, ResponseTime: {ResponseTime}ms, Expiry: {Expiry}, DataSize: {DataSize}",
            cacheName, cacheLevel, key, responseTime.TotalMilliseconds, expiry?.ToString() ?? "None", dataSize);
    }

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
        logger.LogDebug(CacheEvictEventId,
            "Cache evict: {CacheName} [{CacheLevel}] Key: {Key}, ResponseTime: {ResponseTime}ms",
            cacheName, cacheLevel, key, responseTime.TotalMilliseconds);
    }

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
        logger.LogInformation(CacheReloadEventId,
            "Cache reload: {CacheName} Key: {Key}, ResponseTime: {ResponseTime}ms, Expiry: {Expiry}",
            cacheName, key, responseTime.TotalMilliseconds, expiry?.ToString() ?? "None");
    }

    /// <summary>
    /// 记录缓存清空日志
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="responseTime">响应时间</param>
    public static void LogCacheClear(this ILogger logger, string cacheName, TimeSpan responseTime)
    {
        logger.LogDebug(CacheClearEventId,
            "Cache clear: {CacheName}, ResponseTime: {ResponseTime}ms",
            cacheName, responseTime.TotalMilliseconds);
    }

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
            logger.LogDebug(DataSourceLoadEventId,
                "Data source load success: {CacheName} Key: {Key}, ResponseTime: {ResponseTime}ms, DataSize: {DataSize}",
                cacheName, key, responseTime.TotalMilliseconds, dataSize);
        }
        else
        {
            logger.LogWarning(DataSourceLoadEventId,
                "Data source load failed: {CacheName} Key: {Key}, ResponseTime: {ResponseTime}ms",
                cacheName, key, responseTime.TotalMilliseconds);
        }
    }

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
        logger.LogDebug(BatchOperationEventId,
            "Batch operation: {CacheName} Operation: {Operation}, KeyCount: {KeyCount}, SuccessCount: {SuccessCount}, ResponseTime: {ResponseTime}ms",
            cacheName, operation, keyCount, successCount, responseTime.TotalMilliseconds);
    }

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
        logger.LogError(CacheErrorEventId, exception,
            "Cache error: {CacheName} Operation: {Operation}, Key: {Key}, ResponseTime: {ResponseTime}ms, Error: {ErrorMessage}",
            cacheName, operation, key, responseTime.TotalMilliseconds, exception.Message);
    }

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
            logger.LogDebug("Cache health check passed: {CacheName}, ResponseTime: {ResponseTime}ms, Details: {Details}",
                cacheName, responseTime.TotalMilliseconds, details ?? "OK");
        }
        else
        {
            logger.LogWarning("Cache health check failed: {CacheName}, ResponseTime: {ResponseTime}ms, Details: {Details}",
                cacheName, responseTime.TotalMilliseconds, details ?? "Unknown error");
        }
    }
}