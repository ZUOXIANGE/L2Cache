using L2Cache.Examples.Controllers;
using L2Cache.Examples.Models;
using L2Cache.Examples.Services;
using L2Cache.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace L2Cache.Tests.Functional.Examples.Controllers
{
    /// <summary>
    /// ProductController 的测试类
    /// 测试产品相关的控制器逻辑
    /// </summary>
    public class ProductControllerTests
    {
        private readonly Mock<ProductCacheService> _mockProductCache;
        private readonly Mock<ILogger<ProductController>> _mockLogger;
        private readonly ProductController _controller;

        public ProductControllerTests()
        {
            // 初始化 Mock 对象
            _mockLogger = new Mock<ILogger<ProductController>>();

            // 初始化 ProductCacheService 的 Mock
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
            _controller = new ProductController(
                _mockProductCache.Object,
                _mockLogger.Object
            );
        }

        /// <summary>
        /// 测试 Get 方法
        /// 当传入有效的 ID 时，应返回 ActionResult<ProductDto>
        /// </summary>
        [Fact]
        public async Task Get_ValidId_ReturnsActionResult()
        {
            // Arrange
            var id = 1001;
            var product = new ProductDto { Id = id, Name = "Test Product" };

            // 模拟 GetOrLoadAsync 返回产品
            _mockProductCache.Setup(x => x.GetOrLoadAsync(id, It.IsAny<TimeSpan?>()))
                .ReturnsAsync(product);

            // Act
            var result = await _controller.Get(id);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var returnProduct = Assert.IsType<ProductDto>(okResult.Value);
            Assert.Equal(id, returnProduct.Id);
        }
    }
}
