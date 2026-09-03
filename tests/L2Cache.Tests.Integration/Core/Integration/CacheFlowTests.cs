using L2Cache.Abstractions;
using L2Cache.Abstractions.Policies;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 多级缓存交互流程测试
/// <para>测试 L1 和 L2 缓存之间的数据同步和回填逻辑</para>
/// </summary>
public class CacheFlowTests
{
    /// <summary>
    /// 测试：当 L1 未命中但 L2 命中时，GetAsync 应自动回填 L1
    /// </summary>
    [Test]
    public async Task GetAsync_Should_Populate_L1_When_L2_Hit()
    {
        // Arrange
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = "l2_hit_key";
        var value = "l2_value";
        var fullKey = host.FullKey(key);

        // 直接写入 Redis（绕过 L1；L2 值为 JSON 序列化，字符串带引号）
        await host.Db.StringSetAsync(fullKey, $"\"{value}\"");

        // Act
        var result = await host.Client.GetAsync(key);

        // Assert
        await Assert.That(result).IsEqualTo(value);

        // 验证 L1 已被回填：直接删除 L2 后再次读取，仍能命中 L1
        await host.Db.KeyDeleteAsync(fullKey);
        var l1Result = await host.Client.GetAsync(key);
        await Assert.That(l1Result).IsEqualTo(value);
    }

    /// <summary>
    /// 测试：当 L1 和 L2 都未命中时，GetOrLoadAsync 应回源并回填 L1 和 L2
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_LoadFromSource_And_Fill_L1_L2_When_Both_Miss()
    {
        // Arrange：关闭失效广播，避免 Pub/Sub 异步清除本节点 L1 干扰 L1 回填验证
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureRegion: region => region.PublishInvalidation = false);

        var key = "full_miss_key";
        var fullKey = host.FullKey(key);

        // Act
        var result = await host.Client.GetOrLoadAsync(key);

        // Assert
        await Assert.That(result).IsEqualTo($"db_{key}");
        await Assert.That(host.Counter.LoadCount).IsEqualTo(1);

        // Verify L2
        var l2Value = await host.Db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsTrue();
        await Assert.That(l2Value.ToString()).Contains($"db_{key}");

        // Verify L1：删除 L2 后 GetAsync 仍命中，说明回源时已回填 L1
        await host.Db.KeyDeleteAsync(fullKey);
        var l1Result = await host.Client.GetAsync(key);
        await Assert.That(l1Result).IsEqualTo($"db_{key}");
    }

    /// <summary>测试用的复杂对象。</summary>
    public class TestComplexObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>复杂对象加载器：返回固定结构的对象。</summary>
    private sealed class ComplexObjectLoader : ILoader<string, TestComplexObject>
    {
        public Task<TestComplexObject?> LoadAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<TestComplexObject?>(new TestComplexObject
            {
                Id = 1,
                Name = $"name_{key}",
                CreatedAt = new DateTime(2023, 1, 1)
            });

        public Task<Dictionary<string, TestComplexObject>> LoadManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, TestComplexObject>();
            foreach (var key in keys)
            {
                result[key] = new TestComplexObject
                {
                    Id = 1,
                    Name = $"name_{key}",
                    CreatedAt = new DateTime(2023, 1, 1)
                };
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 测试：复杂对象序列化
    /// <para>默认宿主只注册 string -> string，复杂对象区域需自行搭建 DI。</para>
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Handle_ComplexObjects()
    {
        // Arrange
        var regionName = $"complex_flow_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
            {
                options.UseLocalCache = true;
                options.UseRedis = true;
                options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
            })
            .AddCache<string, TestComplexObject>(regionName, region => region.PublishInvalidation = false)
            .WithLoader(_ => new ComplexObjectLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<string, TestComplexObject>>();

        var key = "complex_key";
        var fullKey = $"{regionName}:{key}";

        // Act
        var result = await client.GetOrLoadAsync(key);

        // Assert：返回值正确
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(1);
        await Assert.That(result.Name).IsEqualTo($"name_{key}");

        // Assert：L2 已写入 JSON 序列化数据
        using var redis = ConnectionMultiplexer.Connect(GlobalTestSetup.RedisConnectionString);
        var db = redis.GetDatabase();
        var l2Value = await db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsTrue();
        await Assert.That(l2Value.ToString()).Contains($"name_{key}");

        // Assert：L1 已回填（删除 L2 后仍可读取完整对象）
        await db.KeyDeleteAsync(fullKey);
        var l1Result = await client.GetAsync(key);
        await Assert.That(l1Result).IsNotNull();
        await Assert.That(l1Result!.Name).IsEqualTo($"name_{key}");
    }

    /// <summary>
    /// 测试：过期时间是否生效（L1 与 L2 均按指定过期时间失效）
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Respect_Expiration()
    {
        // Arrange
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = "expire_key";
        var fullKey = host.FullKey(key);
        var expiry = TimeSpan.FromMilliseconds(500);

        // Act 1: Load with short expiry
        await host.Client.GetOrLoadAsync(key, expiry);

        // Verify exists
        await Assert.That(await host.Client.ExistsAsync(key)).IsTrue();

        // Wait for expiration
        await Task.Delay(1000);

        // Verify L1 expired
        await Assert.That(await host.Client.ExistsAsync(key)).IsFalse();

        // Verify L2 expired
        var l2Value = await host.Db.StringGetAsync(fullKey);
        await Assert.That(l2Value.HasValue).IsFalse();
    }

    /// <summary>始终抛出异常的加载器。</summary>
    private sealed class ThrowingLoader : ILoader<string, string>
    {
        public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Source failed");

        public Task<Dictionary<string, string>> LoadManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Source failed");
    }

    /// <summary>
    /// 测试：当回源抛出异常时，异常应冒泡
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_Should_Propagate_Exception_From_Source()
    {
        // Arrange：通过 configureBuilder 追加注册抛异常的 Loader（同类型后注册的生效）
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureBuilder: builder => builder.WithLoader(_ => new ThrowingLoader()));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.Client.GetOrLoadAsync("error_key");
        });
    }
}
