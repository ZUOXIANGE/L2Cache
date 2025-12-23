using System.Diagnostics;
using L2Cache.Abstractions.Telemetry;

namespace L2Cache.Telemetry;

/// <summary>
/// 空操作遥测提供程序（当未启用遥测时使用）
/// </summary>
public class NoOpTelemetryProvider : ITelemetryProvider
{
    public string ActivitySourceName => string.Empty;

    public bool IsEnabled => false;

    public void Dispose()
    {
    }

    public CacheStatistics? GetCacheStatistics(string cacheName)
    {
        return null;
    }

    public Dictionary<string, CacheStatistics> GetAllCacheStatistics()
    {
        return new Dictionary<string, CacheStatistics>();
    }

    public void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public void RecordBatchOperation(string cacheName, string operation, int keyCount, TimeSpan responseTime, int successCount)
    {
    }

    public void RecordCacheError(string cacheName, string operation, Exception error, TimeSpan responseTime)
    {
    }

    public void RecordCacheOperation(string cacheName, CacheOperation operation, string key, CacheLevel? level = null, bool? hit = null, TimeSpan? duration = null, long? size = null, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public void RecordDataSourceLoad(string cacheName, string key, TimeSpan responseTime, bool success)
    {
    }

    public void RecordEvent(string name, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public void RecordException(Exception exception, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public void RecordHistogram(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public void ResetAllStatistics()
    {
    }

    public void ResetStatistics(string cacheName)
    {
    }

    public void SetGauge(string name, double value, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
    }

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, ActivityContext parentContext = default, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        return null;
    }

    public void RecordCacheMetrics(CachePerformanceMetrics metrics)
    {
    }

    public IDisposable CreateTimer(string name, IEnumerable<KeyValuePair<string, object>>? tags = null)
    {
        return new NoOpDisposable();
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
