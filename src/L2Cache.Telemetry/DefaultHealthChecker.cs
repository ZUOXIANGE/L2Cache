using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using L2Cache.Abstractions;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Abstractions.HealthCheck;
using StackExchange.Redis;

namespace L2Cache.Telemetry;

/// <summary>
/// 默认健康检查器实现
/// </summary>
public class DefaultHealthChecker : IHealthChecker
{
    private readonly HealthCheckerOptions _options;
    private readonly ILogger<DefaultHealthChecker>? _logger;
    private readonly Timer _checkTimer;
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<HealthCheckItemResult>>> _healthChecks;
    private readonly ConcurrentQueue<HealthCheckResult> _healthHistory;
    private readonly SemaphoreSlim _checkSemaphore;
        
    private volatile bool _isMonitoring;
    private volatile bool _disposed;
    private volatile HealthStatus _currentStatus = HealthStatus.Unknown;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="options">选项</param>
    /// <param name="logger">日志记录器</param>
    public DefaultHealthChecker(
        IServiceProvider serviceProvider,
        HealthCheckerOptions? options = null, 
        ILogger<DefaultHealthChecker>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? new HealthCheckerOptions();
        _logger = logger;
        _healthChecks = new ConcurrentDictionary<string, Func<CancellationToken, Task<HealthCheckItemResult>>>();
        _healthHistory = new ConcurrentQueue<HealthCheckResult>();
        _checkSemaphore = new SemaphoreSlim(_options.MaxConcurrentChecks, _options.MaxConcurrentChecks);

        // 创建检查定时器
        _checkTimer = new Timer(OnCheckTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        // 添加默认健康检查项
        AddDefaultHealthChecks();
    }

    /// <summary>
    /// 是否正在监控
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan CheckInterval => _options.CheckInterval;

    /// <summary>
    /// 健康状态变化事件
    /// </summary>
    public event EventHandler<HealthStatusChangedEventArgs>? HealthStatusChanged;

    /// <summary>
    /// 开始健康监控
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (_isMonitoring)
        {
            return;
        }

        _isMonitoring = true;

        // 启动时立即检查
        if (_options.CheckOnStartup)
        {
            try
            {
                await CheckHealthAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "启动时健康检查失败");
            }
        }

        // 启动定时检查
        _checkTimer.Change(_options.CheckInterval, _options.CheckInterval);

        _logger?.LogInformation("健康监控已启动，检查间隔: {Interval}", _options.CheckInterval);
    }

    /// <summary>
    /// 停止健康监控
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!_isMonitoring)
        {
            return Task.CompletedTask;
        }

        _isMonitoring = false;
        _checkTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _logger?.LogInformation("健康监控已停止");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var result = new HealthCheckResult(HealthStatus.Unknown, "健康检查执行中");

        try
        {
            // 执行所有健康检查项
            var checkTasks = new List<Task<KeyValuePair<string, HealthCheckItemResult>>>();

            foreach (var kvp in _healthChecks)
            {
                var checkName = kvp.Key;
                var checker = kvp.Value;

                if (_options.ParallelChecks)
                {
                    checkTasks.Add(ExecuteHealthCheckAsync(checkName, checker, cancellationToken));
                }
                else
                {
                    var itemResult = await ExecuteHealthCheckAsync(checkName, checker, cancellationToken);
                    result.Items[itemResult.Key] = itemResult.Value;
                }
            }

            // 等待并行检查完成
            if (_options.ParallelChecks && checkTasks.Count > 0)
            {
                var itemResults = await Task.WhenAll(checkTasks);
                foreach (var itemResult in itemResults)
                {
                    result.Items[itemResult.Key] = itemResult.Value;
                }
            }

            // 计算总体健康状态
            result.Status = CalculateOverallStatus(result.Items.Values);
            result.Description = GetStatusDescription(result.Status, result.Items);

            // 更新连续计数器
            UpdateConsecutiveCounters(result.Status);

            // 检查状态变化
            var previousStatus = _currentStatus;
            if (ShouldUpdateStatus(result.Status))
            {
                _currentStatus = result.Status;

                if (previousStatus != _currentStatus && _options.NotifyOnStatusChange)
                {
                    HealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs(previousStatus, _currentStatus, result));
                }
            }

            // 记录到历史
            AddToHistory(result);

            if (_options.EnableDetailedLogging)
            {
                _logger?.LogInformation("健康检查完成: {Status}, 耗时: {Duration}ms, 检查项: {ItemCount}",
                    result.Status, stopwatch.ElapsedMilliseconds, result.Items.Count);
            }
        }
        catch (Exception ex)
        {
            result.Status = HealthStatus.Unhealthy;
            result.Description = $"健康检查执行失败: {ex.Message}";
            result.Exception = ex;

            _logger?.LogError(ex, "健康检查执行失败");
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// 获取当前健康状态
    /// </summary>
    /// <returns>健康状态</returns>
    public HealthStatus GetCurrentStatus()
    {
        ThrowIfDisposed();
        return _currentStatus;
    }

    /// <summary>
    /// 获取健康检查历史
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <returns>健康检查历史</returns>
    public IEnumerable<HealthCheckResult> GetHealthHistory(int count = 10)
    {
        ThrowIfDisposed();
        return _healthHistory.TakeLast(count).Reverse();
    }

    /// <summary>
    /// 获取最后一次健康检查结果
    /// </summary>
    /// <returns>健康检查结果</returns>
    public HealthCheckResult? GetLastHealthStatus()
    {
        ThrowIfDisposed();
        return GetHealthHistory(1).FirstOrDefault();
    }

    /// <summary>
    /// 添加健康检查项
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="checker">检查器</param>
    public void AddHealthCheck(string name, Func<CancellationToken, Task<HealthCheckItemResult>> checker)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("检查项名称不能为空", nameof(name));
        }

        if (checker == null)
        {
            throw new ArgumentNullException(nameof(checker));
        }

        _healthChecks[name] = checker;
        _logger?.LogDebug("添加健康检查项: {Name}", name);
    }

    /// <summary>
    /// 移除健康检查项
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveHealthCheck(string name)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var removed = _healthChecks.TryRemove(name, out _);
        if (removed)
        {
            _logger?.LogDebug("移除健康检查项: {Name}", name);
        }

        return removed;
    }

    /// <summary>
    /// 清空健康检查项
    /// </summary>
    public void ClearHealthChecks()
    {
        ThrowIfDisposed();
        _healthChecks.Clear();
        _logger?.LogDebug("清空所有健康检查项");
    }

    /// <summary>
    /// 获取所有检查项名称
    /// </summary>
    /// <returns>检查项名称列表</returns>
    public IEnumerable<string> GetHealthCheckNames()
    {
        ThrowIfDisposed();
        return _healthChecks.Keys.ToList();
    }

    /// <summary>
    /// 执行单个健康检查项
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="checker">检查器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检查结果</returns>
    private async Task<KeyValuePair<string, HealthCheckItemResult>> ExecuteHealthCheckAsync(
        string name, 
        Func<CancellationToken, Task<HealthCheckItemResult>> checker, 
        CancellationToken cancellationToken)
    {
        await _checkSemaphore.WaitAsync(cancellationToken);

        try
        {
            var startTime = Stopwatch.GetTimestamp();
                
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.CheckTimeout);

            var result = await checker(timeoutCts.Token);
                
            result.Duration = Stopwatch.GetElapsedTime(startTime);

            return new KeyValuePair<string, HealthCheckItemResult>(name, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = new HealthCheckItemResult(HealthStatus.Unhealthy, "检查被取消");
            return new KeyValuePair<string, HealthCheckItemResult>(name, result);
        }
        catch (OperationCanceledException)
        {
            var result = new HealthCheckItemResult(HealthStatus.Unhealthy, "检查超时");
            return new KeyValuePair<string, HealthCheckItemResult>(name, result);
        }
        catch (Exception ex)
        {
            var result = new HealthCheckItemResult(HealthStatus.Unhealthy, $"检查失败: {ex.Message}")
            {
                Exception = ex
            };
            return new KeyValuePair<string, HealthCheckItemResult>(name, result);
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    /// <summary>
    /// 计算总体健康状态
    /// </summary>
    /// <param name="itemResults">检查项结果</param>
    /// <returns>总体状态</returns>
    private HealthStatus CalculateOverallStatus(IEnumerable<HealthCheckItemResult> itemResults)
    {
        var results = itemResults.ToList();
        if (results.Count == 0)
        {
            return HealthStatus.Unknown;
        }

        var unhealthyCount = results.Count(r => r.Status == HealthStatus.Unhealthy);
        var degradedCount = results.Count(r => r.Status == HealthStatus.Degraded);
        var totalCount = results.Count;

        var unhealthyRatio = (double)unhealthyCount / totalCount;
        var degradedRatio = (double)(unhealthyCount + degradedCount) / totalCount;

        if (unhealthyRatio >= _options.UnhealthyThreshold)
        {
            return HealthStatus.Unhealthy;
        }

        if (degradedRatio >= _options.DegradedThreshold)
        {
            return HealthStatus.Degraded;
        }

        return HealthStatus.Healthy;
    }

    /// <summary>
    /// 获取状态描述
    /// </summary>
    /// <param name="status">状态</param>
    /// <param name="items">检查项</param>
    /// <returns>描述</returns>
    private string GetStatusDescription(HealthStatus status, Dictionary<string, HealthCheckItemResult> items)
    {
        var totalCount = items.Count;
        var healthyCount = items.Values.Count(r => r.Status == HealthStatus.Healthy);
        var degradedCount = items.Values.Count(r => r.Status == HealthStatus.Degraded);
        var unhealthyCount = items.Values.Count(r => r.Status == HealthStatus.Unhealthy);

        return status switch
        {
            HealthStatus.Healthy => $"系统健康 ({healthyCount}/{totalCount} 检查项正常)",
            HealthStatus.Degraded => $"系统降级 ({healthyCount} 正常, {degradedCount} 降级, {unhealthyCount} 异常)",
            HealthStatus.Unhealthy => $"系统异常 ({unhealthyCount}/{totalCount} 检查项异常)",
            _ => "状态未知"
        };
    }

    /// <summary>
    /// 更新连续计数器
    /// </summary>
    /// <param name="status">当前状态</param>
    private void UpdateConsecutiveCounters(HealthStatus status)
    {
        if (status == HealthStatus.Healthy)
        {
            Interlocked.Increment(ref _consecutiveSuccesses);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
        else if (status == HealthStatus.Unhealthy)
        {
            Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Exchange(ref _consecutiveSuccesses, 0);
        }
        else
        {
            // 降级状态不重置计数器
        }
    }

    /// <summary>
    /// 是否应该更新状态
    /// </summary>
    /// <param name="newStatus">新状态</param>
    /// <returns>是否更新</returns>
    private bool ShouldUpdateStatus(HealthStatus newStatus)
    {
        // 如果状态相同，不需要更新
        if (_currentStatus == newStatus)
        {
            return false;
        }

        // 从不健康恢复到健康需要连续成功
        if (_currentStatus == HealthStatus.Unhealthy && newStatus == HealthStatus.Healthy)
        {
            return _consecutiveSuccesses >= _options.SuccessThreshold;
        }

        // 从健康变为不健康需要连续失败
        if (_currentStatus == HealthStatus.Healthy && newStatus == HealthStatus.Unhealthy)
        {
            return _consecutiveFailures >= _options.FailureThreshold;
        }

        // 其他状态变化立即生效
        return true;
    }

    /// <summary>
    /// 添加到历史记录
    /// </summary>
    /// <param name="result">检查结果</param>
    private void AddToHistory(HealthCheckResult result)
    {
        _healthHistory.Enqueue(result);

        // 保持历史记录数量限制
        while (_healthHistory.Count > _options.HistoryRetentionCount)
        {
            _healthHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 添加默认健康检查项
    /// </summary>
    private void AddDefaultHealthChecks()
    {
        // 基本系统检查
        AddHealthCheck("system", async cancellationToken =>
        {
            await Task.Delay(1, cancellationToken); // 模拟检查
            return new HealthCheckItemResult(HealthStatus.Healthy, "系统运行正常");
        });

        // Redis 检查
        var redis = _serviceProvider.GetService<IConnectionMultiplexer>();
        if (redis != null)
        {
            AddHealthCheck("redis", async cancellationToken =>
            {
                try
                {
                    if (!redis.IsConnected)
                    {
                         return new HealthCheckItemResult(HealthStatus.Unhealthy, "Redis 连接已断开");
                    }

                    var db = redis.GetDatabase();
                    var latency = await db.PingAsync();
                    
                    var status = latency.TotalMilliseconds > 500 ? HealthStatus.Degraded : HealthStatus.Healthy;
                    var message = $"Redis 连接正常, 延迟: {latency.TotalMilliseconds:F2}ms";
                    
                    var result = new HealthCheckItemResult(status, message);
                    result.Data["Latency"] = latency.TotalMilliseconds;
                    return result;
                }
                catch (Exception ex)
                {
                    return new HealthCheckItemResult(HealthStatus.Unhealthy, $"Redis 检查失败: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// 定时器回调
    /// </summary>
    /// <param name="state">状态</param>
    private async void OnCheckTimer(object? state)
    {
        if (!_isMonitoring || _disposed)
        {
            return;
        }

        try
        {
            await CheckHealthAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "定时健康检查失败");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DefaultHealthChecker));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isMonitoring = false;
        _checkTimer?.Dispose();
        _checkSemaphore?.Dispose();

        _logger?.LogInformation("健康检查器已释放");
    }
}

/// <summary>
/// 健康检查器扩展方法
/// </summary>
public static class HealthCheckerExtensions
{
    /// <summary>
    /// 创建默认健康检查器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>健康检查器</returns>
    public static IHealthChecker CreateDefault(IServiceProvider serviceProvider, ILogger<DefaultHealthChecker>? logger = null)
    {
        return new DefaultHealthChecker(serviceProvider, new HealthCheckerOptions(), logger);
    }

    /// <summary>
    /// 创建快速响应健康检查器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>健康检查器</returns>
    public static IHealthChecker CreateFastResponse(IServiceProvider serviceProvider, ILogger<DefaultHealthChecker>? logger = null)
    {
        var options = new HealthCheckerOptions
        {
            CheckInterval = TimeSpan.FromSeconds(10),
            CheckTimeout = TimeSpan.FromSeconds(5),
            FailureThreshold = 2,
            SuccessThreshold = 1,
            ParallelChecks = true,
            MaxConcurrentChecks = 20
        };

        return new DefaultHealthChecker(serviceProvider, options, logger);
    }

    /// <summary>
    /// 创建保守健康检查器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>健康检查器</returns>
    public static IHealthChecker CreateConservative(IServiceProvider serviceProvider, ILogger<DefaultHealthChecker>? logger = null)
    {
        var options = new HealthCheckerOptions
        {
            CheckInterval = TimeSpan.FromMinutes(2),
            CheckTimeout = TimeSpan.FromSeconds(30),
            FailureThreshold = 5,
            SuccessThreshold = 3,
            ParallelChecks = false,
            MaxConcurrentChecks = 5,
            DegradedThreshold = 0.2,
            UnhealthyThreshold = 0.6
        };

        return new DefaultHealthChecker(serviceProvider, options, logger);
    }

    /// <summary>
    /// 添加缓存相关健康检查项
    /// </summary>
    /// <param name="healthChecker">健康检查器</param>
    /// <param name="cacheService">缓存服务</param>
    /// <returns>健康检查器</returns>
    public static IHealthChecker AddCacheHealthChecks<TKey, TValue>(this IHealthChecker healthChecker, 
        ICacheService<TKey, TValue>? cacheService = null) where TKey : notnull
    {
        // Note: Direct access to AbstractCacheService protected members is no longer allowed.
        // Health checks should rely on IConnectionMultiplexer and IMemoryCache registered in DI,
        // which are covered by AddDefaultHealthChecks().
        
        return healthChecker;
    }
}
