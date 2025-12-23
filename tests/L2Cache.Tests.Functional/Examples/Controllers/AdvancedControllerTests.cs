using FluentAssertions;
using L2Cache.Examples.Controllers;
using L2Cache.Examples.Services;
using L2Cache.Serializers.Json;
using L2Cache.Abstractions.Telemetry;
using L2Cache.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using L2Cache.Examples.Models;

namespace L2Cache.Tests.Functional.Examples.Controllers
{
    /// <summary>
    /// AdvancedController 的测试类
    /// 测试高级功能，如切换序列化器和获取缓存统计信息
    /// </summary>
    public class AdvancedControllerTests
    {
        private readonly Mock<ProductCacheService> _mockProductCache;
        private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
        private readonly Mock<ILogger<AdvancedController>> _mockLogger;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly AdvancedController _controller;

        public AdvancedControllerTests()
        {
            // 初始化 Mock 对象
            _mockTelemetryProvider = new Mock<ITelemetryProvider>();
            _mockLogger = new Mock<ILogger<AdvancedController>>();
            _mockRedis = new Mock<IConnectionMultiplexer>();

            // 初始化 ProductCacheService 的 Mock
            // 因为 ProductCacheService 是具体的类，我们需要提供构造函数参数
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockOptions = new Mock<IOptions<L2CacheOptions>>();
            mockOptions.Setup(x => x.Value).Returns(new L2CacheOptions());
            var mockBaseLogger = new Mock<ILogger<L2CacheService<int, ProductDto>>>();
            var mockServiceLogger = new Mock<ILogger<ProductCacheService>>();

            _mockProductCache = new Mock<ProductCacheService>(
                mockServiceProvider.Object,
                mockOptions.Object,
                mockBaseLogger.Object,
                mockServiceLogger.Object
            );

            // 初始化控制器
            _controller = new AdvancedController(
                _mockProductCache.Object,
                _mockTelemetryProvider.Object,
                _mockLogger.Object,
                _mockRedis.Object
            );
        }

        /// <summary>
        /// 测试 SwitchSerializer 方法
        /// 当传入有效的序列化器类型（如 "json"）时，应返回 OkResult
        /// </summary>
        [Fact]
        public void SwitchSerializer_ShouldReturnOk()
        {
            // Act
            var result = _controller.SwitchSerializer("json");

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().Be("Switched to JSON serializer");
        }

        /// <summary>
        /// 测试 SwitchSerializer 方法
        /// 当传入无效的序列化器类型时，应返回 BadRequest
        /// </summary>
        [Fact]
        public void SwitchSerializer_InvalidType_ShouldReturnBadRequest()
        {
            // Act
            var result = _controller.SwitchSerializer("invalid");

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        /// <summary>
        /// 测试 GetStats 方法
        /// 应返回包含缓存统计信息的 OkResult
        /// </summary>
        [Fact]
        public void GetStats_ShouldReturnOk()
        {
            // Arrange
            // 模拟 ITelemetryProvider.GetCacheStatistics
            var stats = new CacheStatistics();
            _mockTelemetryProvider.Setup(x => x.GetCacheStatistics(It.IsAny<string>())).Returns(stats);

            // 模拟 Redis 连接状态
            _mockRedis.Setup(x => x.IsConnected).Returns(true);

            // Act
            var result = _controller.GetStats();

            // Assert
            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
