using L2Cache.Abstractions;
using L2Cache.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 弹性/韧性测试
/// 测试当Redis不可用时的降级处理和恢复能力
/// </summary>
public class ResilienceTests : IAsyncDisposable
{
    private RedisContainer? _redisContainer;
    private IntegrationTestFactory? _factory;
    private IServiceScope? _scope;

    [Before(Test)]
    public async Task InitializeAsync()
    {
        _redisContainer = new RedisBuilder(new DotNet.Testcontainers.Images.DockerImage("redis:8.0")).Build();
        await _redisContainer.StartAsync();
        InitializeApp();
    }

    [After(Test)]
    public async ValueTask DisposeAsync()
    {
        CleanupApp();
        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }

    private void InitializeApp()
    {
        _factory = new IntegrationTestFactory();
        if (_redisContainer != null)
        {
            _factory.RedisConnectionString = _redisContainer.GetConnectionString();
        }
        _scope = _factory.Services.CreateScope();
    }

    private void CleanupApp()
    {
        _scope?.Dispose();
        _factory?.Dispose();
    }

    /// <summary>
    /// 测试当Redis宕机时，操作应该降级或优雅失败
    /// </summary>
    [Test]
    public async Task When_Redis_Is_Down_Operations_Should_Fallback_Or_Fail_Gracefully()
    {
        // Arrange (准备)
        var cacheService = _scope!.ServiceProvider.GetRequiredService<ICacheService<string, string>>();
        var key = "resilience_test_key";
        var value = "test_value";

        // 1. 确保正常运行
        await cacheService.PutAsync(key, value);
        var result1 = await cacheService.GetAsync(key);
        await Assert.That(result1).IsEqualTo(value);

        // 2. 停止 Redis
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
        }

        // 3. 尝试读取 (如果L1存在，应从L1读取)
        var result2 = await cacheService.GetAsync(key);
        await Assert.That(result2).IsEqualTo(value);

        // 4. 尝试写入 (应该失败或记录错误，但不应导致应用崩溃)
        try
        {
            await cacheService.PutAsync("new_key", "new_value");
        }
        catch
        {
            // 在此处捕获异常是可接受的
        }

        // 5. 重启 Redis
        if (_redisContainer != null)
        {
            await _redisContainer.StartAsync();
        }

        // 重新初始化应用以获取新的连接字符串
        CleanupApp();
        InitializeApp();

        cacheService = _scope!.ServiceProvider.GetRequiredService<ICacheService<string, string>>();

        // 6. 验证恢复
        await cacheService.PutAsync("recovery_key", "recovery_value");
        var result3 = await cacheService.GetAsync("recovery_key");
        await Assert.That(result3).IsEqualTo("recovery_value");
    }
}
