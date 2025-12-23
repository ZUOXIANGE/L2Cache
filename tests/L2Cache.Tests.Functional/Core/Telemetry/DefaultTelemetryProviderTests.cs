using FluentAssertions;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace L2Cache.Tests.Functional.Core.Telemetry;

/// <summary>
/// 默认遥测提供者测试
/// 测试缓存指标收集和统计功能
/// </summary>
public class DefaultTelemetryProviderTests
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

    /// <summary>
    /// 记录缓存命中
    /// </summary>
    [Fact]
    public void RecordCacheOperation_ShouldRecordHit()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.HitCount.Should().Be(1);
        stats.MissCount.Should().Be(0);
        stats.CacheName.Should().Be("test-cache");
    }

    /// <summary>
    /// 记录缓存未命中
    /// </summary>
    [Fact]
    public void RecordCacheOperation_ShouldRecordMiss()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.HitCount.Should().Be(0);
        stats.MissCount.Should().Be(1);
    }

    /// <summary>
    /// 计算命中率
    /// </summary>
    [Fact]
    public void RecordCacheOperation_ShouldCalculateHitRate()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key2", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key3", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheMiss("test-cache", CacheLevel.L1, "key4", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.HitCount.Should().Be(2);
        stats.MissCount.Should().Be(2);
        stats.HitRate.Should().Be(0.5);
    }

    /// <summary>
    /// 记录缓存设置操作
    /// </summary>
    [Fact]
    public void RecordCacheOperation_ShouldRecordSet()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheSet("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.SetCount.Should().Be(1);
    }

    /// <summary>
    /// 记录缓存驱逐操作
    /// </summary>
    [Fact]
    public void RecordCacheOperation_ShouldRecordEvict()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheEvict("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.EvictCount.Should().Be(1);
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    [Fact]
    public void ResetStatistics_ShouldResetCounts()
    {
        // Arrange (准备)
        _telemetryProvider.RecordCacheHit("test-cache", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));
        var statsBefore = _telemetryProvider.GetCacheStatistics("test-cache");
        statsBefore!.HitCount.Should().Be(1);

        // Act (执行)
        _telemetryProvider.ResetStatistics("test-cache");

        // Assert (断言)
        var statsAfter = _telemetryProvider.GetCacheStatistics("test-cache");
        statsAfter.Should().NotBeNull();
        statsAfter!.HitCount.Should().Be(0);
    }

    /// <summary>
    /// 获取所有缓存统计
    /// </summary>
    [Fact]
    public void GetAllCacheStatistics_ShouldReturnAllStats()
    {
        // Arrange (准备)
        _telemetryProvider.RecordCacheHit("cache1", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));
        _telemetryProvider.RecordCacheMiss("cache2", CacheLevel.L1, "key1", TimeSpan.FromMilliseconds(10));

        // Act (执行)
        var allStats = _telemetryProvider.GetAllCacheStatistics();

        // Assert (断言)
        allStats.Should().HaveCount(2);
        allStats.Should().ContainKey("cache1");
        allStats.Should().ContainKey("cache2");
        allStats["cache1"].HitCount.Should().Be(1);
        allStats["cache2"].MissCount.Should().Be(1);
    }

    /// <summary>
    /// 记录缓存错误
    /// </summary>
    [Fact]
    public void RecordCacheError_ShouldIncrementErrorCount()
    {
        // Act (执行)
        _telemetryProvider.RecordCacheError("test-cache", "Get", new Exception("test error"), TimeSpan.FromMilliseconds(10));

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.ErrorCount.Should().Be(1);
    }

    /// <summary>
    /// 记录数据源加载
    /// </summary>
    [Fact]
    public void RecordDataSourceLoad_ShouldUpdateStats()
    {
        // Act (执行)
        _telemetryProvider.RecordDataSourceLoad("test-cache", "key1", TimeSpan.FromMilliseconds(50), true);
        _telemetryProvider.RecordDataSourceLoad("test-cache", "key2", TimeSpan.FromMilliseconds(50), false);

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics("test-cache");
        stats.Should().NotBeNull();
        stats!.DataSourceLoadCount.Should().Be(2);
        stats.DataSourceLoadSuccessCount.Should().Be(1);
    }

    /// <summary>
    /// 记录批量操作
    /// </summary>
    [Fact]
    public void RecordBatchOperation_ShouldUpdateStats()
    {
        // Act (执行)
        _telemetryProvider.RecordBatchOperation("test-cache", "BatchGet", 10, TimeSpan.FromMilliseconds(100), 8);

        // Assert (断言)
        // 验证调用不抛出异常
    }

    /// <summary>
    /// 并发安全性测试
    /// </summary>
    [Fact]
    public void Concurrency_ShouldBeThreadSafe()
    {
        // Arrange (准备)
        int threadCount = 10;
        int iterationsPerThread = 100;
        string cacheName = "concurrent-cache";

        // Act (执行)
        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                _telemetryProvider.RecordCacheHit(cacheName, CacheLevel.L1, "key", TimeSpan.FromMilliseconds(1));
            }
        });

        // Assert (断言)
        var stats = _telemetryProvider.GetCacheStatistics(cacheName);
        stats.Should().NotBeNull();
        stats!.HitCount.Should().Be(threadCount * iterationsPerThread);
    }
}
