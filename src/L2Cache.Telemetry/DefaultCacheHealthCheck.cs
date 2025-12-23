using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Configuration;
using L2Cache.Abstractions.HealthCheck;

namespace L2Cache.Telemetry;

/// <summary>
/// 默认缓存健康检查实现
/// </summary>
public class DefaultCacheHealthCheck : ICacheHealthCheck
{
    private readonly IDatabase _database;
    private readonly IMemoryCache? _memoryCache;
    private readonly ITelemetryProvider? _telemetry;
    private readonly ILogger<DefaultCacheHealthCheck>? _logger;
    private readonly string _cacheName;
    private readonly DateTime _startTime;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="database">Redis数据库实例</param>
    /// <param name="cacheName">缓存名称</param>
    /// <param name="memoryCache">本地缓存实例</param>
    /// <param name="telemetry">遥测提供程序</param>
    /// <param name="logger">日志记录器</param>
    public DefaultCacheHealthCheck(
        IDatabase database,
        string cacheName,
        IMemoryCache? memoryCache = null,
        ITelemetryProvider? telemetry = null,
        ILogger<DefaultCacheHealthCheck>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _cacheName = cacheName ?? throw new ArgumentNullException(nameof(cacheName));
        _memoryCache = memoryCache;
        _telemetry = telemetry;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 检查缓存服务的健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<CacheHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var startTime = Stopwatch.GetTimestamp();
            
        try
        {
            _logger?.LogDebug("开始缓存健康检查 - {CacheName}", _cacheName);

            // 检查Redis健康状态
            var redisResult = await CheckRedisHealthAsync(cancellationToken);
                
            // 检查本地缓存健康状态
            var localResult = await CheckLocalCacheHealthAsync(cancellationToken);

            var elapsed = Stopwatch.GetElapsedTime(startTime);

            // 综合评估健康状态
            var overallStatus = DetermineOverallStatus(redisResult.Status, localResult.Status);
            var description = BuildOverallDescription(redisResult, localResult);

            var result = CacheHealthCheckResult.Create(
                overallStatus,
                description,
                (long)elapsed.TotalMilliseconds);

            result.Data["redis"] = redisResult;
            result.Data["localCache"] = localResult;

            _logger?.LogDebug("缓存健康检查完成 - {CacheName}, 状态: {Status}, 耗时: {ElapsedMs}ms", 
                _cacheName, overallStatus, (long)elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger?.LogError(ex, "缓存健康检查失败 - {CacheName}", _cacheName);
                
            return CacheHealthCheckResult.Unhealthy(
                $"缓存健康检查失败: {ex.Message}",
                ex,
                (long)elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// 检查Redis连接状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Redis健康检查结果</returns>
    public async Task<CacheHealthCheckResult> CheckRedisHealthAsync(CancellationToken cancellationToken = default)
    {
        var startTime = Stopwatch.GetTimestamp();
            
        try
        {
            // 检查Redis连接状态
            if (!_database.Multiplexer.IsConnected)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                return CacheHealthCheckResult.Unhealthy(
                    "Redis连接已断开",
                    responseTimeMs: (long)elapsed.TotalMilliseconds);
            }

            // 执行PING命令测试连接
            var pingResult = await _database.PingAsync();
            var elapsedTotal = Stopwatch.GetElapsedTime(startTime);

            var responseTimeMs = (long)elapsedTotal.TotalMilliseconds;
            var pingTimeMs = pingResult.TotalMilliseconds;

            var result = CacheHealthCheckResult.Healthy(
                $"Redis连接正常, Ping: {pingTimeMs:F2}ms",
                responseTimeMs);

            result.Data["pingTimeMs"] = pingTimeMs;
            result.Data["isConnected"] = true;
            result.Data["endpoints"] = _database.Multiplexer.GetEndPoints().Select(ep => ep.ToString()).ToArray();

            // 检查响应时间是否过长
            if (pingTimeMs > 1000) // 超过1秒认为是降级状态
            {
                result.Status = CacheHealthStatus.Degraded;
                result.Description = $"Redis响应较慢, Ping: {pingTimeMs:F2}ms";
            }

            return result;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger?.LogWarning(ex, "Redis健康检查失败 - {CacheName}", _cacheName);
                
            return CacheHealthCheckResult.Unhealthy(
                $"Redis健康检查失败: {ex.Message}",
                ex,
                (long)elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// 检查本地缓存状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本地缓存健康检查结果</returns>
    public async Task<CacheHealthCheckResult> CheckLocalCacheHealthAsync(CancellationToken cancellationToken = default)
    {
        var startTime = Stopwatch.GetTimestamp();
            
        try
        {
            if (_memoryCache == null)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                return CacheHealthCheckResult.Healthy(
                    "本地缓存未启用",
                    (long)elapsed.TotalMilliseconds);
            }

            // 测试本地缓存的读写操作
            var testKey = $"__health_check_{_cacheName}_{Guid.NewGuid()}";
            var testValue = DateTime.UtcNow.ToString();

            // 写入测试
            _memoryCache.Set(testKey, testValue, TimeSpan.FromSeconds(10));

            // 读取测试
            var retrievedValue = _memoryCache.Get<string>(testKey);
                
            // 清理测试数据
            _memoryCache.Remove(testKey);

            var elapsedTotal = Stopwatch.GetElapsedTime(startTime);

            if (retrievedValue == testValue)
            {
                var result = CacheHealthCheckResult.Healthy(
                    "本地缓存工作正常",
                    (long)elapsedTotal.TotalMilliseconds);

                result.Data["isAvailable"] = true;
                result.Data["testSuccessful"] = true;

                return result;
            }
            else
            {
                return CacheHealthCheckResult.Degraded(
                    "本地缓存读写测试失败",
                    (long)elapsedTotal.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger?.LogWarning(ex, "本地缓存健康检查失败 - {CacheName}", _cacheName);
                
            return CacheHealthCheckResult.Degraded(
                $"本地缓存健康检查失败: {ex.Message}",
                (long)elapsed.TotalMilliseconds,
                ex);
        }
    }

    /// <summary>
    /// 获取缓存服务的详细状态信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存状态信息</returns>
    public async Task<CacheStatusInfo> GetCacheStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusInfo = new CacheStatusInfo
        {
            CacheName = _cacheName,
            LastCheckTime = DateTime.UtcNow,
            Uptime = DateTime.UtcNow - _startTime
        };

        try
        {
            // Redis状态信息
            statusInfo.IsRedisConnected = _database.Multiplexer.IsConnected;
            if (statusInfo.IsRedisConnected)
            {
                var endpoints = _database.Multiplexer.GetEndPoints();
                statusInfo.RedisServerInfo = string.Join(", ", endpoints.Select(ep => ep.ToString()));
            }

            // 本地缓存状态信息
            statusInfo.IsLocalCacheAvailable = _memoryCache != null;
            if (_memoryCache != null)
            {
                // 注意：MemoryCache没有直接的统计API，这里提供基本信息
                statusInfo.LocalCacheStats["type"] = _memoryCache.GetType().Name;
                statusInfo.LocalCacheStats["isAvailable"] = true;
            }

            // 缓存指标信息
            if (_telemetry != null)
            {
                var stats = _telemetry.GetCacheStatistics(_cacheName);
                if (stats != null)
                {
                    statusInfo.CacheMetrics["hitCount"] = stats.HitCount;
                    statusInfo.CacheMetrics["missCount"] = stats.MissCount;
                    statusInfo.CacheMetrics["hitRate"] = stats.HitRate;
                    statusInfo.CacheMetrics["setCount"] = stats.SetCount;
                    statusInfo.CacheMetrics["evictCount"] = stats.EvictCount;
                    statusInfo.CacheMetrics["errorCount"] = stats.ErrorCount;
                    statusInfo.CacheMetrics["dataSourceLoadCount"] = stats.DataSourceLoadCount;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取缓存状态信息失败 - {CacheName}", _cacheName);
        }

        return statusInfo;
    }

    /// <summary>
    /// 确定整体健康状态
    /// </summary>
    /// <param name="redisStatus">Redis状态</param>
    /// <param name="localStatus">本地缓存状态</param>
    /// <returns>整体健康状态</returns>
    private static CacheHealthStatus DetermineOverallStatus(CacheHealthStatus redisStatus, CacheHealthStatus localStatus)
    {
        // Redis是主要的缓存，其状态权重更高
        if (redisStatus == CacheHealthStatus.Unhealthy)
        {
            return CacheHealthStatus.Unhealthy;
        }

        if (redisStatus == CacheHealthStatus.Degraded || localStatus == CacheHealthStatus.Degraded)
        {
            return CacheHealthStatus.Degraded;
        }

        if (localStatus == CacheHealthStatus.Unhealthy)
        {
            return CacheHealthStatus.Degraded; // 本地缓存不健康只是降级，不是完全不可用
        }

        return CacheHealthStatus.Healthy;
    }

    /// <summary>
    /// 构建整体描述信息
    /// </summary>
    /// <param name="redisResult">Redis检查结果</param>
    /// <param name="localResult">本地缓存检查结果</param>
    /// <returns>整体描述信息</returns>
    private static string BuildOverallDescription(CacheHealthCheckResult redisResult, CacheHealthCheckResult localResult)
    {
        var descriptions = new List<string>();

        if (!string.IsNullOrEmpty(redisResult.Description))
        {
            descriptions.Add($"Redis: {redisResult.Description}");
        }

        if (!string.IsNullOrEmpty(localResult.Description))
        {
            descriptions.Add($"本地缓存: {localResult.Description}");
        }

        return string.Join("; ", descriptions);
    }
}
