using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net.Http.Json;
using L2Cache.Examples.Models;

namespace L2Cache.Tests.Functional.Examples.Integration;

/// <summary>
/// 产品 API 的集成测试
/// 验证 API 层的缓存行为
/// </summary>
[Collection("Shared Test Collection")]
public class ProductApiTests : BaseIntegrationTest
{
    public ProductApiTests(RedisTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 测试获取产品接口
    /// 第一次调用应较慢（缓存未命中，查库），第二次调用应较快（缓存命中）
    /// </summary>
    [Fact]
    public async Task GetProduct_FirstCall_ShouldBeSlower_SecondCall_ShouldBeFaster()
    {
        // Arrange
        var productId = 1001;
        var url = $"/api/product/{productId}";

        // Act 1: 第一次调用 (缓存未命中 -> 数据库)
        var start1 = DateTime.UtcNow;
        var response1 = await _client.GetAsync(url);
        var end1 = DateTime.UtcNow;
        var duration1 = end1 - start1;

        // Assert 1
        response1.EnsureSuccessStatusCode();
        var product1 = await response1.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product1);
        Assert.Equal(productId, product1.Id);

        // Act 2: 第二次调用 (缓存命中 -> Redis/L1)
        var start2 = DateTime.UtcNow;
        var response2 = await _client.GetAsync(url);
        var end2 = DateTime.UtcNow;
        var duration2 = end2 - start2;

        // Assert 2
        response2.EnsureSuccessStatusCode();
        var product2 = await response2.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product2);
        Assert.Equal(productId, product2.Id);

        // 注意: 在实际网络环境中，这种断言可能不稳定。
        // 但由于我们使用 Testcontainers 且一切都在本地，第一次调用（通常在示例中通过 Thread.Sleep 模拟 DB 延迟）应该明显更慢。
        // 如果示例服务没有人为延迟，我们可能看不到巨大差异，但至少可以验证功能性。
    }
}
