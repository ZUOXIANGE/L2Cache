using System.Diagnostics;
using L2Cache.Abstractions.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using StackExchange.Redis;

namespace L2Cache.Tests.Functional.Core.Telemetry;

/// <summary>
/// 抽象缓存服务追踪测试
/// 测试 AbstractCacheService 中的遥测和活动追踪逻辑
/// </summary>
public class AbstractCacheServiceTracingTests
{
    public class TestCacheService : AbstractCacheService<string, string>
    {
        private readonly ITelemetryProvider _telemetryProvider;

        public TestCacheService(ITelemetryProvider telemetryProvider)
        {
            _telemetryProvider = telemetryProvider;
        }

        public override string GetCacheName() => "TestCache";
        protected override ITelemetryProvider? GetTelemetryProvider() => _telemetryProvider;
        protected override IDatabase? GetRedisDatabase() => null;
        protected override IMemoryCache? GetLocalCache() => null;
        protected override Task<string?> QueryDataAsync(string key) => Task.FromResult<string?>(null);
        protected override Task<Dictionary<string, string>> QueryDataListAsync(List<string> keyList) => Task.FromResult(new Dictionary<string, string>());
        protected override Task UpdateDataAsync(string key, string value) => Task.CompletedTask;
    }

    /// <summary>
    /// 测试 GetAsync 应该启动活动(Activity)
    /// </summary>
    [Fact]
    public async Task GetAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        // 我们返回 null activity 以简化测试，验证调用即可。
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheGet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.GetAsync("test-key");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheGet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 GetAsync 应该记录指标(Metrics)
    /// </summary>
    [Fact]
    public async Task GetAsync_ShouldRecordMetrics()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.GetAsync("test-key");

        // Assert (断言)
        // 由于两个缓存都为null，逻辑如下：
        // 1. 跳过 L1 检查
        // 2. 跳过 L2 检查
        // 3. 记录 L2 未命中 (Miss) 并返回默认值
        
        // 验证 RecordCacheOperation 被调用 (记录 L2 Miss)
        telemetryMock.Verify(x => x.RecordCacheOperation(
            "TestCache",
            CacheOperation.Get,
            "test-key",
            CacheLevel.L2,
            false, // hit = false (miss)
            It.IsAny<TimeSpan?>(),
            It.IsAny<long?>(),
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 PutAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task PutAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheSet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.PutAsync("test-key", "test-value");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheSet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);

        // 验证记录 Set 操作
        telemetryMock.Verify(x => x.RecordCacheOperation(
            "TestCache",
            CacheOperation.Set,
            "test-key",
            CacheLevel.L2,
            null,
            It.IsAny<TimeSpan?>(),
            It.IsAny<long?>(),
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 EvictAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task EvictAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheEvict, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.EvictAsync("test-key");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheEvict, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);

        // 验证记录 Evict 操作
        telemetryMock.Verify(x => x.RecordCacheOperation(
            "TestCache",
            CacheOperation.Evict,
            "test-key",
            CacheLevel.L2,
            null,
            It.IsAny<TimeSpan?>(),
            It.IsAny<long?>(),
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 GetOrLoadAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task GetOrLoadAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheGetOrLoad, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.GetOrLoadAsync("test-key");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheGetOrLoad, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 PutIfAbsentAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task PutIfAbsentAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CachePutIfAbsent, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.PutIfAbsentAsync("test-key", "test-value");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CachePutIfAbsent, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 ReloadAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task ReloadAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheReload, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.ReloadAsync("test-key");

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheReload, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 BatchGetAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task BatchGetAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchGet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.BatchGetAsync(["key1", "key2"]);

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchGet, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 BatchGetOrLoadAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task BatchGetOrLoadAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchGetOrLoad, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.BatchGetOrLoadAsync(["key1", "key2"]);

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchGetOrLoad, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }

    /// <summary>
    /// 测试 BatchEvictAsync 应该启动活动
    /// </summary>
    [Fact]
    public async Task BatchEvictAsync_ShouldStartActivity()
    {
        // Arrange (准备)
        var telemetryMock = new Mock<ITelemetryProvider>();
        telemetryMock.Setup(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchDelete, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()))
            .Returns((Activity?)null);

        var service = new TestCacheService(telemetryMock.Object);

        // Act (执行)
        await service.BatchEvictAsync(["key1", "key2"]);

        // Assert (断言)
        telemetryMock.Verify(x => x.StartActivity(
            TelemetryConstants.ActivityNames.CacheBatchDelete, 
            It.IsAny<ActivityKind>(), 
            It.IsAny<ActivityContext>(), 
            It.IsAny<IEnumerable<KeyValuePair<string, object>>>()), Times.Once);
    }
}
