using FluentAssertions;
using L2Cache.Telemetry;
using L2Cache.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace L2Cache.Tests.Functional.Telemetry;

/// <summary>
/// 默认健康检查器测试
/// 测试 Redis 和缓存系统的健康检查逻辑
/// </summary>
public class DefaultHealthCheckerTests
{
    private readonly Mock<ILogger<DefaultHealthChecker>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly HealthCheckerOptions _options;
    private readonly DefaultHealthChecker _healthChecker;

    public DefaultHealthCheckerTests()
    {
        _mockLogger = new Mock<ILogger<DefaultHealthChecker>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _options = new HealthCheckerOptions
        {
            CheckInterval = TimeSpan.FromSeconds(1),
            FailureThreshold = 3
        };
        _healthChecker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, _mockLogger.Object);
    }

    /// <summary>
    /// 构造函数应成功创建实例
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act (执行)
        var healthChecker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, _mockLogger.Object);

        // Assert (断言)
        healthChecker.Should().NotBeNull();
    }

    /// <summary>
    /// 构造函数：ServiceProvider 为 null 时应抛出异常
    /// </summary>
    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert (执行 & 断言)
        Assert.Throws<ArgumentNullException>(() => 
            new DefaultHealthChecker(null!, _options, _mockLogger.Object));
    }

    /// <summary>
    /// 构造函数：Logger 为 null 时不应抛出异常 (Logger 是可选的)
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrowArgumentNullException()
    {
        // Act & Assert (执行 & 断言)
        var checker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, null!);
        checker.Should().NotBeNull();
    }

    /// <summary>
    /// 构造函数：Options 为 null 时应使用默认配置
    /// </summary>
    [Fact]
    public void Constructor_WithNullOptions_ShouldUseDefaultOptions()
    {
        // Act (执行)
        var checker = new DefaultHealthChecker(_mockServiceProvider.Object, null, _mockLogger.Object);
        
        // Assert (断言)
        checker.Should().NotBeNull();
        checker.CheckInterval.Should().Be(TimeSpan.FromSeconds(30)); // 默认值
    }

    /// <summary>
    /// 检查健康状态：基本配置下应返回健康
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WithValidConfiguration_ShouldReturnHealthy()
    {
        // Act (执行)
        var result = await _healthChecker.CheckHealthAsync(CancellationToken.None);

        // Assert (断言)
        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 检查健康状态：Redis 连接正常时应返回健康
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WithRedisConnected_ShouldReturnHealthy()
    {
        // Arrange (准备)
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        
        mockMultiplexer.Setup(x => x.IsConnected).Returns(true);
        mockMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockDb.Setup(x => x.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(10));

        _mockServiceProvider.Setup(x => x.GetService(typeof(IConnectionMultiplexer)))
            .Returns(mockMultiplexer.Object);

        var healthChecker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, _mockLogger.Object);

        // Act (执行)
        var result = await healthChecker.CheckHealthAsync(CancellationToken.None);

        // Assert (断言)
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Items.Should().ContainKey("redis");
        result.Items["redis"].Status.Should().Be(HealthStatus.Healthy);
    }

    /// <summary>
    /// 检查健康状态：Redis 断开时应返回不健康
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WithRedisDisconnected_ShouldReturnUnhealthy()
    {
        // Arrange (准备)
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(x => x.IsConnected).Returns(false);

        _mockServiceProvider.Setup(x => x.GetService(typeof(IConnectionMultiplexer)))
            .Returns(mockMultiplexer.Object);

        var healthChecker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, _mockLogger.Object);

        // Act (执行)
        var result = await healthChecker.CheckHealthAsync(CancellationToken.None);

        // Assert (断言)
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Items.Should().ContainKey("redis");
        result.Items["redis"].Status.Should().Be(HealthStatus.Unhealthy);
    }

    /// <summary>
    /// 检查健康状态：应遵循取消令牌
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange (准备)
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert (执行 & 断言)
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _healthChecker.CheckHealthAsync(cts.Token));
    }

    /// <summary>
    /// 测试启动监控
    /// </summary>
    [Fact]
    public async Task StartMonitoringAsync_ShouldStartHealthMonitoring()
    {
        // Act (执行)
        await _healthChecker.StartMonitoringAsync(CancellationToken.None);

        // Assert (断言)
        _healthChecker.IsMonitoring.Should().BeTrue();
    }

    /// <summary>
    /// 测试停止监控
    /// </summary>
    [Fact]
    public async Task StopMonitoringAsync_ShouldStopHealthMonitoring()
    {
        // Arrange (准备)
        await _healthChecker.StartMonitoringAsync(CancellationToken.None);

        // Act (执行)
        await _healthChecker.StopMonitoringAsync(CancellationToken.None);

        // Assert (断言)
        _healthChecker.IsMonitoring.Should().BeFalse();
    }
}
