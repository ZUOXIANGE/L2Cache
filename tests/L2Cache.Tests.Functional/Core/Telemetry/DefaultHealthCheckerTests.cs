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
            CheckInterval = TimeSpan.FromSeconds(1)
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

    /// <summary>
    /// 测试添加和移除健康检查项
    /// </summary>
    [Fact]
    public async Task AddAndRemoveHealthCheck_ShouldWorkCorrectly()
    {
        // Arrange
        var checkName = "test_check";
        var checker = new Func<CancellationToken, Task<HealthCheckItemResult>>(ct => 
            Task.FromResult(new HealthCheckItemResult(HealthStatus.Healthy, "OK")));

        // Act - Add
        _healthChecker.AddHealthCheck(checkName, checker);
        
        // Assert - Add
        _healthChecker.GetHealthCheckNames().Should().Contain(checkName);

        // Act - Execute to verify it runs
        var result = await _healthChecker.CheckHealthAsync();
        result.Items.Should().ContainKey(checkName);

        // Act - Remove
        var removed = _healthChecker.RemoveHealthCheck(checkName);

        // Assert - Remove
        removed.Should().BeTrue();
        _healthChecker.GetHealthCheckNames().Should().NotContain(checkName);
    }

    /// <summary>
    /// 测试自定义检查抛出异常时应被捕获并标记为不健康
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WithThrowingCheck_ShouldHandleException()
    {
        // Arrange
        _healthChecker.AddHealthCheck("failing_check", ct => throw new Exception("Boom!"));

        // Act
        var result = await _healthChecker.CheckHealthAsync();

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Items.Should().ContainKey("failing_check");
        result.Items["failing_check"].Status.Should().Be(HealthStatus.Unhealthy);
        result.Items["failing_check"].Description.Should().Contain("Boom!");
    }

    /// <summary>
    /// 测试历史记录功能
    /// </summary>
    [Fact]
    public async Task GetHealthHistory_ShouldReturnRecentResults()
    {
        // Arrange
        // 运行几次检查
        await _healthChecker.CheckHealthAsync();
        await _healthChecker.CheckHealthAsync();
        await _healthChecker.CheckHealthAsync();

        // Act
        var history = _healthChecker.GetHealthHistory(2).ToList();

        // Assert
        history.Should().HaveCount(2);
        history.All(x => x.Status != HealthStatus.Unknown).Should().BeTrue();
    }

    /// <summary>
    /// 测试状态变更事件
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_OnStatusChange_ShouldRaiseEvent()
    {
        // Arrange
        var eventRaised = false;
        HealthStatus? oldStatus = null;
        HealthStatus? newStatus = null;

        _healthChecker.HealthStatusChanged += (sender, args) =>
        {
            eventRaised = true;
            oldStatus = args.PreviousStatus;
            newStatus = args.CurrentStatus;
        };

        // 添加一个控制状态的检查
        var isHealthy = true;
        _healthChecker.AddHealthCheck("toggle_check", ct => 
            Task.FromResult(new HealthCheckItemResult(
                isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy, 
                "Toggle")));

        // Act 1: 初始检查 (Unknown -> Healthy)
        await _healthChecker.CheckHealthAsync();
        
        // Assert 1
        eventRaised.Should().BeTrue();
        oldStatus.Should().Be(HealthStatus.Unknown);
        newStatus.Should().Be(HealthStatus.Healthy);

        // Reset
        eventRaised = false;

        // Act 2: 变为不健康 (Healthy -> Unhealthy)
        isHealthy = false;
        await _healthChecker.CheckHealthAsync();

        // Assert 2
        eventRaised.Should().BeTrue();
        oldStatus.Should().Be(HealthStatus.Healthy);
        newStatus.Should().Be(HealthStatus.Unhealthy);
    }
}
