using L2Cache.Abstractions.Telemetry;
using L2Cache.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace L2Cache.Tests.Unit.Telemetry;

/// <summary>
/// 默认遥测提供者测试
/// 测试缓存指标收集和统计功能
/// </summary>
public class DefaultTelemetryProviderTests : IAsyncDisposable
{
    private readonly Mock<ILogger<DefaultTelemetryProvider>> _mockLogger;
    private readonly IOptions<TelemetryOptions> _options;
    private readonly DefaultTelemetryProvider _telemetryProvider;

    public DefaultTelemetryProviderTests()
    {
        _mockLogger = new Mock<ILogger<DefaultTelemetryProvider>>();
        _options = Options.Create(new TelemetryOptions
        {
            EnableMetrics = true,
            EnableTracing = true
        });
        _telemetryProvider = new DefaultTelemetryProvider(_options.Value, _mockLogger.Object);
    }

    [After(Test)]
    public async ValueTask DisposeAsync()
    {
        _telemetryProvider.Dispose();
        GC.SuppressFinalize(this);
        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// 记录缓存命中
    /// </summary>
    [Test]
    public async Task RecordCacheOperation_ShouldRecordHit()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        await Assert.That(stats).IsNotNull();
        await Assert.That(stats!.HitCount).IsEqualTo(1);
        await Assert.That(stats.MissCount).IsEqualTo(0);
        await Assert.That(stats.CacheName).IsEqualTo("test-cache");
    }

    /// <summary>
    /// 记录缓存未命中
    /// </summary>
    [Test]
    public async Task RecordCacheOperation_ShouldRecordMiss()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        await Assert.That(stats).IsNotNull();
        await Assert.That(stats!.HitCount).IsEqualTo(0);
        await Assert.That(stats.MissCount).IsEqualTo(1);
    }

    /// <summary>
    /// 计算命中率
    /// </summary>
    [Test]
    public async Task RecordCacheOperation_ShouldCalculateHitRate()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key2", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key3", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key4", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        await Assert.That(stats).IsNotNull();
        await Assert.That(stats!.HitCount).IsEqualTo(2);
        await Assert.That(stats.MissCount).IsEqualTo(2);
        await Assert.That(stats.HitRate).IsEqualTo(0.5);
    }

    /// <summary>
    /// 记录缓存设置操作
    /// </summary>
    [Test]
    public async Task RecordCacheOperation_ShouldRecordSet()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheSet("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        await Assert.That(stats).IsNotNull();
        await Assert.That(stats!.SetCount).IsEqualTo(1);
    }
}
