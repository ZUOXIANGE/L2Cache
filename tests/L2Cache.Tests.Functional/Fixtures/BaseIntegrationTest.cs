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
    protected HttpClient Client { get; }
    protected IntegrationTestFactory Factory { get; }
    protected IServiceScope Scope { get; }

    protected BaseIntegrationTest(RedisTestFixture fixture)
    {
        // 使用 RedisTestFixture 中的连接字符串初始化工厂
        Factory = new IntegrationTestFactory();
        Factory.RedisConnectionString = fixture.ConnectionString;
        Client = Factory.CreateClient();
        Scope = Factory.Services.CreateScope();
    }

    public void Dispose()
    {
        Scope.Dispose();
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 从当前 Scope 获取服务
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        return Scope.ServiceProvider.GetRequiredService<T>();
    }
}
