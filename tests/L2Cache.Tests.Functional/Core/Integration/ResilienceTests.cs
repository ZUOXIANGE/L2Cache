using L2Cache.Abstractions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 弹性/韧性测试
/// 测试当Redis不可用时的降级处理和恢复能力
/// </summary>
// 不使用共享测试集合，因为我们需要独立控制Redis容器的生命周期
public class ResilienceTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer;
    private IntegrationTestFactory? _factory;
    private HttpClient? _client;
    private IServiceScope? _scope;

    public ResilienceTests()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7.0")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        InitializeApp();
    }

    private void InitializeApp()
    {
        _factory = new IntegrationTestFactory();
        _factory.RedisConnectionString = _redisContainer.GetConnectionString();
        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
    }

    private void CleanupApp()
    {
        _scope?.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }

    public async Task DisposeAsync()
    {
        CleanupApp();
        await _redisContainer.DisposeAsync();
    }

    /// <summary>
    /// 测试当Redis宕机时，操作应该降级或优雅失败
    /// </summary>
    [Fact]
    public async Task When_Redis_Is_Down_Operations_Should_Fallback_Or_Fail_Gracefully()
    {
        // Arrange (准备)
        var cacheService = _scope!.ServiceProvider.GetRequiredService<ICacheService<string, string>>();
        var key = "resilience_test_key";
        var value = "test_value";

        // 1. 确保正常运行
        await cacheService.PutAsync(key, value);
        var result1 = await cacheService.GetAsync(key);
        Assert.Equal(value, result1);

        // 2. 停止 Redis
        // 注意: 停止容器可能会导致重启后端口变更，
        // StackExchange.Redis 的自动重连可能因端口变更而失效。
        // 此测试主要验证应用在 Redis 消失时的降级行为。
        await _redisContainer.StopAsync();

        // 3. 尝试读取 (如果L1存在，应从L1读取)
        var result2 = await cacheService.GetAsync(key);
        Assert.Equal(value, result2); 

        // 4. 尝试写入 (应该失败或记录错误，但不应导致应用崩溃)
        // 我们期望 L2Cache 内部处理 Redis 异常 (记录日志)，并尽可能继续 (例如只写 L1)，
        // 或者根据配置抛出异常。
        // 假设默认行为：尝试写入 Redis 并失败。
        // 验证不会导致测试进程崩溃。
        try 
        {
            await cacheService.PutAsync("new_key", "new_value");
        }
        catch
        {
            // 在此处捕获异常是可接受的
        }
        
        // 5. 重启 Redis
        await _redisContainer.StartAsync();

        // 由于端口可能已变更，我们需要重新初始化应用以获取新的连接字符串
        // 这模拟了应用程序重启或配置重新加载。
        // 如果我们想测试自动重连，需要确保端口保持不变 (例如绑定固定宿主端口)。
        CleanupApp();
        InitializeApp();
        
        cacheService = _scope!.ServiceProvider.GetRequiredService<ICacheService<string, string>>();

        // 6. 验证恢复
        await cacheService.PutAsync("recovery_key", "recovery_value");
        var result3 = await cacheService.GetAsync("recovery_key");
        Assert.Equal("recovery_value", result3);
    }
}
