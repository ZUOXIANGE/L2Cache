using DotNet.Testcontainers.Images;
using Testcontainers.Redis;
using TUnit.Core;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<L2Cache.Tests.Integration.IntegrationParallelLimit>]

namespace L2Cache.Tests.Integration;

/// <summary>
/// 限制集成测试的并行度：并行测试各自创建 Redis 连接并产生线程池压力，
/// 全速并行会触发 SE.Redis 默认 5s 超时（锁释放、订阅等），导致测试间相互干扰。
/// </summary>
public sealed class IntegrationParallelLimit : IParallelLimit
{
    public int Limit => 4;
}

public class GlobalTestSetup
{
    private static RedisContainer? Container;

    public static string RedisConnectionString => Container?.GetConnectionString() ?? throw new InvalidOperationException("Redis container not initialized");

    [Before(Assembly)]
    public static async Task InitializeAsync()
    {
        Container = new RedisBuilder(new DockerImage("redis:8.0")).Build();

        await Container.StartAsync();
    }

    [After(Assembly)]
    public static async Task DisposeAsync()
    {
        if (Container != null)
        {
            await Container.DisposeAsync();
        }
    }
}
