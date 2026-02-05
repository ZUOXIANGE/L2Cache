using DotNet.Testcontainers.Images;
using Testcontainers.Redis;

namespace L2Cache.Tests.Integration;

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
