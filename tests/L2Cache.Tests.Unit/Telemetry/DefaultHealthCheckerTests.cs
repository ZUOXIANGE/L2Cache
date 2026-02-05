using L2Cache.Abstractions.Telemetry;
using L2Cache.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;

namespace L2Cache.Tests.Unit.Telemetry;

/// <summary>
/// 默认健康检查器测试
/// 测试 Redis 和缓存系统的健康检查逻辑
/// </summary>
public class DefaultHealthCheckerTests : IDisposable
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

    public void Dispose()
    {
        _healthChecker.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 构造函数应成功创建实例
    /// </summary>
    [Test]
    public async Task Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act (执行)
        var healthChecker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, _mockLogger.Object);

        // Assert (断言)
        await Assert.That(healthChecker).IsNotNull();
    }

    /// <summary>
    /// 构造函数：ServiceProvider 为 null 时应抛出异常
    /// </summary>
    [Test]
    public async Task Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert (执行 & 断言)
        Action act = () => { _ = new DefaultHealthChecker(null!, _options, _mockLogger.Object); };
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// 构造函数：Logger 为 null 时不应抛出异常 (Logger 是可选的)
    /// </summary>
    [Test]
    public async Task Constructor_WithNullLogger_ShouldNotThrowArgumentNullException()
    {
        // Act & Assert (执行 & 断言)
        var checker = new DefaultHealthChecker(_mockServiceProvider.Object, _options, null!);
        await Assert.That(checker).IsNotNull();
    }

    /// <summary>
    /// 构造函数：Options 为 null 时应使用默认配置
    /// </summary>
    [Test]
    public async Task Constructor_WithNullOptions_ShouldUseDefaultOptions()
    {
        // Act (执行)
        var checker = new DefaultHealthChecker(_mockServiceProvider.Object, null, _mockLogger.Object);

        // Assert (断言)
        await Assert.That(checker).IsNotNull();
        await Assert.That(checker.CheckInterval).IsEqualTo(TimeSpan.FromSeconds(30)); // 默认值
    }

    /// <summary>
    /// 检查健康状态：基本配置下应返回健康
    /// </summary>
    [Test]
    public async Task CheckHealthAsync_WithValidConfiguration_ShouldReturnHealthy()
    {
        // Act (执行)
        var result = await _healthChecker.CheckHealthAsync(CancellationToken.None);

        // Assert (断言)
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        // TUnit 不支持 CloseTo，这里简化为范围检查
        await Assert.That(result.Timestamp).IsGreaterThan(DateTime.UtcNow.AddSeconds(-5));
        await Assert.That(result.Timestamp).IsLessThan(DateTime.UtcNow.AddSeconds(5));
    }
}
