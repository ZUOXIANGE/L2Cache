using L2Cache.Examples.Controllers;
using L2Cache.Abstractions.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using HealthStatus = L2Cache.Abstractions.Telemetry.HealthStatus;
using HealthCheckResult = L2Cache.Abstractions.Telemetry.HealthCheckResult;

namespace L2Cache.Tests.Functional.Examples.Controllers
{
    /// <summary>
    /// TelemetryController 的测试类
    /// 测试健康检查和指标获取功能
    /// </summary>
    public class TelemetryControllerTests
    {
        private readonly Mock<IHealthChecker> _mockHealthChecker;
        private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
        private readonly TelemetryController _controller;

        public TelemetryControllerTests()
        {
            // 初始化 Mock 对象
            _mockHealthChecker = new Mock<IHealthChecker>();
            _mockTelemetryProvider = new Mock<ITelemetryProvider>();

            // 初始化控制器
            _controller = new TelemetryController(
                _mockHealthChecker.Object,
                _mockTelemetryProvider.Object
            );
        }

        /// <summary>
        /// 测试 GetHealth 方法
        /// 当健康检查结果为 Healthy 时，应返回 OkResult
        /// </summary>
        [Fact]
        public async Task GetHealth_Healthy_ReturnsOkResult()
        {
            // Arrange
            var healthResult = new HealthCheckResult(HealthStatus.Healthy, "Healthy");
            _mockHealthChecker.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(healthResult);

            // Act
            var result = await _controller.GetHealth();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnResult = Assert.IsType<HealthCheckResult>(okResult.Value);
            Assert.Equal(HealthStatus.Healthy, returnResult.Status);
        }

        /// <summary>
        /// 测试 GetHealth 方法
        /// 当健康检查结果不为 Healthy 时，应返回 StatusCode 503 (ServiceUnavailable)
        /// </summary>
        [Fact]
        public async Task GetHealth_Unhealthy_ReturnsServiceUnavailable()
        {
            // Arrange
            var healthResult = new HealthCheckResult(HealthStatus.Unhealthy, "Unhealthy");
            _mockHealthChecker.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(healthResult);

            // Act
            var result = await _controller.GetHealth();

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(503, objectResult.StatusCode);
            var returnResult = Assert.IsType<HealthCheckResult>(objectResult.Value);
            Assert.Equal(HealthStatus.Unhealthy, returnResult.Status);
        }

        /// <summary>
        /// 测试 GetMetrics 方法
        /// 应返回包含指标数据的 OkResult
        /// </summary>
        [Fact]
        public void GetMetrics_ReturnsOkResult()
        {
            // Arrange
            // 模拟 GetAllCacheStatistics 返回字典
            var metrics = new Dictionary<string, CacheStatistics>();
            _mockTelemetryProvider.Setup(x => x.GetAllCacheStatistics()).Returns(metrics);

            // Act
            var result = _controller.GetMetrics();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnMetrics = Assert.IsType<Dictionary<string, CacheStatistics>>(okResult.Value);
            Assert.NotNull(returnMetrics);
        }
    }
}
