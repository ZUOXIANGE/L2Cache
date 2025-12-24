using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using L2Cache.Abstractions;
using L2Cache.Abstractions.Telemetry;
using StackExchange.Redis;

namespace L2Cache.Telemetry;

/// <summary>
/// 默认健康检查器实现（简化版）
/// </summary>
public class DefaultHealthChecker : IHealthChecker
{
    private readonly HealthCheckerOptions _options;
    private readonly ILogger<DefaultHealthChecker>? _logger;
    private readonly Timer _checkTimer;
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<HealthCheckItemResult>>> _healthChecks;
    private readonly ConcurrentQueue<HealthCheckResult> _healthHistory;
        
    private volatile bool _isMonitoring;
    private volatile bool _disposed;
    private volatile HealthStatus _currentStatus = HealthStatus.Unknown;

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 构造函数
    /// </summary>
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

        // 创建检查定时器
        _checkTimer = new Timer(OnCheckTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        // 添加默认健康检查项
        AddDefaultHealthChecks();
    }

    /// <inheritdoc />
    public bool IsMonitoring => _isMonitoring;

    /// <inheritdoc />
    public TimeSpan CheckInterval => _options.CheckInterval;

    /// <inheritdoc />
    public event EventHandler<HealthStatusChangedEventArgs>? HealthStatusChanged;

    /// <inheritdoc />
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isMonitoring) return;

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

    /// <inheritdoc />
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isMonitoring) return Task.CompletedTask;

        _isMonitoring = false;
        _checkTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _logger?.LogInformation("健康监控已停止");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var result = new HealthCheckResult(HealthStatus.Unknown, "健康检查执行中");

        try
        {
            // 并行执行所有检查
            var tasks = _healthChecks.Select(kvp => ExecuteHealthCheckAsync(kvp.Key, kvp.Value, cancellationToken));
            var results = await Task.WhenAll(tasks);

            foreach (var item in results)
            {
                result.Items[item.Key] = item.Value;
            }

            // 计算总体状态：只要有一个不健康，整体就不健康
            result.Status = results.Any(x => x.Value.Status != HealthStatus.Healthy) 
                ? HealthStatus.Unhealthy 
                : HealthStatus.Healthy;
            
            result.Description = result.Status == HealthStatus.Healthy 
                ? "系统健康" 
                : $"系统异常: {string.Join(", ", results.Where(r => r.Value.Status != HealthStatus.Healthy).Select(r => r.Key))}";

            // 状态变化通知
            var previousStatus = _currentStatus;
            if (_currentStatus != result.Status)
            {
                _currentStatus = result.Status;
                if (_options.NotifyOnStatusChange)
                {
                    HealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs(previousStatus, _currentStatus, result));
                }
            }

            // 记录历史
            AddToHistory(result);

            if (_options.EnableDetailedLogging)
            {
                _logger?.LogInformation("健康检查完成: {Status}, 耗时: {Duration}ms", result.Status, stopwatch.ElapsedMilliseconds);
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

    /// <inheritdoc />
    public HealthStatus GetCurrentStatus() => _currentStatus;

    /// <inheritdoc />
    public IEnumerable<HealthCheckResult> GetHealthHistory(int count = 10)
    {
        return _healthHistory.TakeLast(count).Reverse();
    }

    /// <inheritdoc />
    public void AddHealthCheck(string name, Func<CancellationToken, Task<HealthCheckItemResult>> checker)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("名称不能为空", nameof(name));
        _healthChecks[name] = checker ?? throw new ArgumentNullException(nameof(checker));
    }

    /// <inheritdoc />
    public bool RemoveHealthCheck(string name)
    {
        return _healthChecks.TryRemove(name, out _);
    }

    /// <inheritdoc />
    public void ClearHealthChecks()
    {
        _healthChecks.Clear();
    }

    /// <inheritdoc />
    public IEnumerable<string> GetHealthCheckNames()
    {
        return _healthChecks.Keys.ToList();
    }

    private async Task<KeyValuePair<string, HealthCheckItemResult>> ExecuteHealthCheckAsync(
        string name, 
        Func<CancellationToken, Task<HealthCheckItemResult>> checker, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CheckTimeout);
            
            var startTime = Stopwatch.GetTimestamp();
            var result = await checker(cts.Token);
            result.Duration = Stopwatch.GetElapsedTime(startTime);
            
            return new KeyValuePair<string, HealthCheckItemResult>(name, result);
        }
        catch (Exception ex)
        {
            return new KeyValuePair<string, HealthCheckItemResult>(name, 
                new HealthCheckItemResult(HealthStatus.Unhealthy, $"检查异常: {ex.Message}") { Exception = ex });
        }
    }

    private void AddToHistory(HealthCheckResult result)
    {
        _healthHistory.Enqueue(result);
        while (_healthHistory.Count > _options.HistoryRetentionCount)
        {
            _healthHistory.TryDequeue(out _);
        }
    }

    private void AddDefaultHealthChecks()
    {
        // Redis 检查
        var redis = _serviceProvider.GetService<IConnectionMultiplexer>();
        if (redis != null)
        {
            AddHealthCheck("redis", async cancellationToken =>
            {
                if (!redis.IsConnected)
                    return new HealthCheckItemResult(HealthStatus.Unhealthy, "Redis 连接已断开");

                var latency = await redis.GetDatabase().PingAsync();
                return new HealthCheckItemResult(HealthStatus.Healthy, $"延迟: {latency.TotalMilliseconds:F2}ms");
            });
        }
    }

    private async void OnCheckTimer(object? state)
    {
        if (!_isMonitoring || _disposed) return;
        try { await CheckHealthAsync(); } catch { /* Ignore background errors */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DefaultHealthChecker));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isMonitoring = false;
        _checkTimer?.Dispose();
    }
}

/// <summary>
/// 健康检查器扩展方法
/// </summary>
public static class HealthCheckerExtensions
{
    public static IHealthChecker CreateDefault(IServiceProvider serviceProvider, ILogger<DefaultHealthChecker>? logger = null)
    {
        return new DefaultHealthChecker(serviceProvider, new HealthCheckerOptions(), logger);
    }
}
