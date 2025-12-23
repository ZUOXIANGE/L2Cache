using Testcontainers.Redis;
using Xunit;

namespace L2Cache.Tests.Functional.Fixtures;

/// <summary>
/// Redis 测试固件
/// 使用 Testcontainers 启动 Redis 容器，确保测试环境隔离且真实
/// </summary>
public class RedisTestFixture : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer;
    
    public RedisTestFixture()
    {
        // 配置 Redis 容器，使用 redis:7.0 镜像
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7.0")
            .Build();
    }

    /// <summary>
    /// 获取 Redis 连接字符串
    /// </summary>
    public string ConnectionString => _redisContainer.GetConnectionString();

    /// <summary>
    /// 初始化：启动 Redis 容器
    /// </summary>
    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
    }

    /// <summary>
    /// 销毁：停止并释放 Redis 容器
    /// </summary>
    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }
}
