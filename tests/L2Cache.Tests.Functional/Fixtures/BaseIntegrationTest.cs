using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace L2Cache.Tests.Functional.Fixtures;

/// <summary>
/// 集成测试基类
/// 提供 HttpClient, Factory 和 Scope 管理
/// </summary>
[Collection("Shared Test Collection")]
public abstract class BaseIntegrationTest : IDisposable
{
    protected readonly HttpClient _client;
    protected readonly IntegrationTestFactory _factory;
    protected readonly IServiceScope _scope;
    
    protected BaseIntegrationTest(RedisTestFixture fixture)
    {
        // 使用 RedisTestFixture 中的连接字符串初始化工厂
        _factory = new IntegrationTestFactory();
        _factory.RedisConnectionString = fixture.ConnectionString;
        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
    }
    
    public void Dispose()
    {
        _scope.Dispose();
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// 从当前 Scope 获取服务
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        return _scope.ServiceProvider.GetRequiredService<T>();
    }
}
