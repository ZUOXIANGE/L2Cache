using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Moq;

namespace L2Cache.Tests.Functional.Fixtures;

/// <summary>
/// 集成测试工厂
/// 用于创建测试用的 WebApplication，并替换 Redis 服务
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public string RedisConnectionString { get; set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 移除现有的 Redis 服务 (IDatabase 和 IConnectionMultiplexer)
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabase));
            if (descriptor != null) services.Remove(descriptor);

            var connectionDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (connectionDescriptor != null) services.Remove(connectionDescriptor);

            if (!string.IsNullOrEmpty(RedisConnectionString))
            {
                // 如果提供了连接字符串（通常来自 Testcontainers），则使用真实的 Redis 连接
                var multiplexer = ConnectionMultiplexer.Connect(RedisConnectionString);
                services.AddSingleton<IConnectionMultiplexer>(multiplexer);
                services.AddSingleton<IDatabase>(multiplexer.GetDatabase());
            }
            else
            {
                // 如果 Docker/Redis 不可用，使用内存中的 Mock 对象模拟 Redis 行为
                var mockDatabase = new Mock<IDatabase>();
                var memoryStore = new Dictionary<string, (string Value, DateTime? Expiry)>();
                
                // 模拟 StringGetAsync
                mockDatabase.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync((RedisKey key, CommandFlags flags) => {
                         if (memoryStore.TryGetValue(key.ToString(), out var val)) {
                             if (val.Expiry.HasValue && val.Expiry < DateTime.UtcNow) {
                                 memoryStore.Remove(key.ToString());
                                 return RedisValue.Null;
                             }
                             return val.Value;
                         }
                         return RedisValue.Null;
                    });

                // 模拟 StringSetAsync
                mockDatabase.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) => {
                        var k = key.ToString();
                        var exists = memoryStore.ContainsKey(k);
                        if (when == When.NotExists && exists) return false;
                        if (when == When.Exists && !exists) return false;
                        
                        DateTime? exp = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null;
                        memoryStore[k] = (value.ToString(), exp);
                        return true;
                    });
                    
                // 模拟 KeyDeleteAsync
                mockDatabase.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync((RedisKey key, CommandFlags flags) => {
                         return memoryStore.Remove(key.ToString());
                    });
                    
                 // 模拟 KeyExistsAsync
                 mockDatabase.Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync((RedisKey key, CommandFlags flags) => {
                         if (memoryStore.TryGetValue(key.ToString(), out var val)) {
                             if (val.Expiry.HasValue && val.Expiry < DateTime.UtcNow) {
                                 memoryStore.Remove(key.ToString());
                                 return false;
                             }
                             return true;
                         }
                         return false;
                    });

                services.AddSingleton<IDatabase>(mockDatabase.Object);
                
                var mockMultiplexer = new Mock<IConnectionMultiplexer>();
                mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
                services.AddSingleton<IConnectionMultiplexer>(mockMultiplexer.Object);
            }
        });

        builder.UseEnvironment("Testing");
    }
}
