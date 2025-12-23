namespace L2Cache.Abstractions.HealthCheck;

/// <summary>
/// 缓存健康检查接口
/// 用于监控缓存服务的健康状态
/// </summary>
public interface ICacheHealthCheck
{
    /// <summary>
    /// 检查缓存服务的健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<CacheHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查Redis连接状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Redis健康检查结果</returns>
    Task<CacheHealthCheckResult> CheckRedisHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查本地缓存状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本地缓存健康检查结果</returns>
    Task<CacheHealthCheckResult> CheckLocalCacheHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取缓存服务的详细状态信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存状态信息</returns>
    Task<CacheStatusInfo> GetCacheStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 缓存健康检查结果
/// </summary>
public class CacheHealthCheckResult
{
    /// <summary>
    /// 健康状态
    /// </summary>
    public CacheHealthStatus Status { get; set; }

    /// <summary>
    /// 描述信息
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 异常信息（如果有）
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 响应时间（毫秒）
    /// </summary>
    public long ResponseTimeMs { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 额外数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 创建健康状态结果
    /// </summary>
    /// <param name="status">健康状态</param>
    /// <param name="description">描述信息</param>
    /// <param name="responseTimeMs">响应时间</param>
    /// <param name="exception">异常信息</param>
    /// <returns>健康检查结果</returns>
    public static CacheHealthCheckResult Create(
        CacheHealthStatus status,
        string description,
        long responseTimeMs = 0,
        Exception? exception = null)
    {
        return new CacheHealthCheckResult
        {
            Status = status,
            Description = description,
            ResponseTimeMs = responseTimeMs,
            Exception = exception
        };
    }

    /// <summary>
    /// 创建健康状态
    /// </summary>
    /// <param name="description">描述信息</param>
    /// <param name="responseTimeMs">响应时间</param>
    /// <returns>健康检查结果</returns>
    public static CacheHealthCheckResult Healthy(string description, long responseTimeMs = 0)
    {
        return Create(CacheHealthStatus.Healthy, description, responseTimeMs);
    }

    /// <summary>
    /// 创建降级状态
    /// </summary>
    /// <param name="description">描述信息</param>
    /// <param name="responseTimeMs">响应时间</param>
    /// <param name="exception">异常信息</param>
    /// <returns>健康检查结果</returns>
    public static CacheHealthCheckResult Degraded(string description, long responseTimeMs = 0, Exception? exception = null)
    {
        return Create(CacheHealthStatus.Degraded, description, responseTimeMs, exception);
    }

    /// <summary>
    /// 创建不健康状态
    /// </summary>
    /// <param name="description">描述信息</param>
    /// <param name="exception">异常信息</param>
    /// <param name="responseTimeMs">响应时间</param>
    /// <returns>健康检查结果</returns>
    public static CacheHealthCheckResult Unhealthy(string description, Exception? exception = null, long responseTimeMs = 0)
    {
        return Create(CacheHealthStatus.Unhealthy, description, responseTimeMs, exception);
    }
}

/// <summary>
/// 缓存健康状态枚举
/// </summary>
public enum CacheHealthStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 降级（部分功能可用）
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy = 2
}

/// <summary>
/// 缓存状态信息
/// </summary>
public class CacheStatusInfo
{
    /// <summary>
    /// 缓存名称
    /// </summary>
    public string CacheName { get; set; } = string.Empty;

    /// <summary>
    /// Redis连接状态
    /// </summary>
    public bool IsRedisConnected { get; set; }

    /// <summary>
    /// 本地缓存状态
    /// </summary>
    public bool IsLocalCacheAvailable { get; set; }

    /// <summary>
    /// Redis服务器信息
    /// </summary>
    public string RedisServerInfo { get; set; } = string.Empty;

    /// <summary>
    /// 本地缓存统计信息
    /// </summary>
    public Dictionary<string, object> LocalCacheStats { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 缓存指标统计
    /// </summary>
    public Dictionary<string, object> CacheMetrics { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 最后检查时间
    /// </summary>
    public DateTime LastCheckTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 运行时长
    /// </summary>
    public TimeSpan Uptime { get; set; }
}