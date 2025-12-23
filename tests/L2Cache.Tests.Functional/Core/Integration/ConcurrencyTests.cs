using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using L2Cache.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 并发测试
/// 测试在高并发场景下的缓存读写一致性和稳定性
/// </summary>
[Collection("Shared Test Collection")]
public class ConcurrencyTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public ConcurrencyTests(RedisTestFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    /// <summary>
    /// 测试并发读取应返回一致的结果
    /// </summary>
    [Fact]
    public async Task Concurrent_Get_Should_Return_Consistent_Result()
    {
        // Arrange (准备)
        var cacheService = GetService<ICacheService<string, string>>();
        var key = $"concurrent_get_{Guid.NewGuid()}";
        var expectedValue = "initial_value";
        
        // 预热缓存
        await cacheService.PutAsync(key, expectedValue);

        // Act (执行)
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () => await cacheService.GetAsync(key)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert (断言)
        Assert.All(results, result => Assert.Equal(expectedValue, result));
    }

    /// <summary>
    /// 测试并发写入不应导致崩溃
    /// </summary>
    [Fact]
    public async Task Concurrent_Put_Should_Not_Crash()
    {
        // Arrange (准备)
        var cacheService = GetService<ICacheService<string, string>>();
        var key = $"concurrent_put_{Guid.NewGuid()}";
        
        // Act (执行)
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            var value = $"value_{i}";
            tasks.Add(Task.Run(async () => await cacheService.PutAsync(key, value)));
        }

        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));

        // Assert (断言)
        Assert.Null(exception);
        
        // 最终值检查 (应该是其中一个值)
        var finalValue = await cacheService.GetAsync(key);
        Assert.NotNull(finalValue);
        Assert.StartsWith("value_", finalValue);
    }
}
