using System.Reflection;
using L2Cache.Abstractions;
using L2Cache.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace L2Cache.Tests.Integration.Core.Integration;

public class PubSubCacheSyncTests
{
    [Test]
    public async Task PutAsync_OnNodeA_ShouldInvalidateL1_OnNodeB()
    {
        // Reset the static flag to ensure Node A and Node B both subscribe
        // This is necessary because they run in the same process/AppDomain in this test,
        // but we want to simulate them as separate nodes (each having its own subscription).
        ResetSubscriptionFlag();

        // Arrange
        var key = "sync-key-" + Guid.NewGuid();
        var value1 = "value-1";
        var value2 = "value-2";
        // Use a unique channel prefix to avoid conflicts with other tests
        var channelPrefix = $"test-sync-{Guid.NewGuid()}";

        // Setup Node A
        var servicesA = CreateServiceProvider(channelPrefix);
        var cacheA = servicesA.GetRequiredService<ICacheService<string, string>>();

        // Reset again so Node B also subscribes (simulating a second process)
        ResetSubscriptionFlag();

        // Setup Node B
        var servicesB = CreateServiceProvider(channelPrefix);
        var cacheB = servicesB.GetRequiredService<ICacheService<string, string>>();

        // 1. Node A writes initial value
        await cacheA.PutAsync(key, value1);

        // 2. Node B reads (populates its L1)
        var valB1 = await cacheB.GetAsync(key);
        await Assert.That(valB1).IsEqualTo(value1);

        // 3. Node A updates value (should trigger Pub -> Node B Sub -> Invalidate L1)
        await cacheA.PutAsync(key, value2);

        // 4. Wait for Pub/Sub propagation
        await Task.Delay(1000);

        // 5. Node B reads again
        var valB2 = await cacheB.GetAsync(key);

        // Assert
        // Node B should have fetched the new value from L2 after L1 invalidation
        await Assert.That(valB2).IsEqualTo(value2);
    }

    private static void ResetSubscriptionFlag()
    {
        var type = typeof(L2CacheService<string, string>);
        var field = type.GetField("IsSubscribed", BindingFlags.Static | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(null, false);
        }
    }

    private static ServiceProvider CreateServiceProvider(string channelPrefix)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder => builder.AddConsole());

        // L2Cache
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;

            // Enable Pub/Sub
            options.PubSub.Enabled = true;
            options.PubSub.ChannelPrefix = channelPrefix;
        });

        return services.BuildServiceProvider();
    }
}
