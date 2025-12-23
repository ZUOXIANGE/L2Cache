namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// Telemetry extensions for convenient cache operation recording
/// </summary>
public static class TelemetryExtensions
{
    public static void RecordCacheHit(this ITelemetryProvider telemetry, string cacheName, CacheLevel cacheLevel, string key, TimeSpan responseTime)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Get, key, cacheLevel, true, responseTime);
    }

    public static void RecordCacheMiss(this ITelemetryProvider telemetry, string cacheName, CacheLevel cacheLevel, string key, TimeSpan responseTime)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Get, key, cacheLevel, false, responseTime);
    }

    public static void RecordCacheSet(this ITelemetryProvider telemetry, string cacheName, CacheLevel cacheLevel, string key, TimeSpan responseTime, long dataSize = 0)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Set, key, cacheLevel, null, responseTime, dataSize);
    }
    
    public static void RecordCacheEvict(this ITelemetryProvider telemetry, string cacheName, CacheLevel cacheLevel, string key, TimeSpan responseTime)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Evict, key, cacheLevel, null, responseTime);
    }

    public static void RecordCacheReload(this ITelemetryProvider telemetry, string cacheName, string key, TimeSpan responseTime, bool success)
    {
        // Map reload to DataSourceLoad or a custom operation?
        // DataSourceLoad seems appropriate as reload usually means loading from source.
        telemetry.RecordDataSourceLoad(cacheName, key, responseTime, success);
    }

    public static void RecordCacheClear(this ITelemetryProvider telemetry, string cacheName, TimeSpan responseTime)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Clear, "*", null, null, responseTime);
    }

    public static void RecordCacheExists(this ITelemetryProvider telemetry, string cacheName, string key, bool exists)
    {
        telemetry.RecordCacheOperation(cacheName, CacheOperation.Exists, key, null, exists, TimeSpan.Zero);
    }
}
