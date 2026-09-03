using L2Cache.Abstractions;
using L2Cache.Abstractions.Policies;
using L2Cache.Abstractions.Stores;
using L2Cache.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace L2Cache.Tests.Unit.Core;

/// <summary>
/// CacheClient / CacheOrchestrator 管道测试（仅 L1，无 Redis）：
/// 覆盖 Cache-Aside 回源、缓存命中、空值缓存、淘汰与批量管道
/// </summary>
public class CacheClientTests
{
    private static (ServiceProvider Provider, LoadCounter Counter) BuildClient(
        Action<CacheRegionOptions>? configureRegion = null,
        Func<LoadCounter, ITestLoader>? loaderFactory = null)
    {
        var counter = new LoadCounter();
        var services = new ServiceCollection();

        var builder = services.AddL2Cache(options => { options.UseLocalCache = true; options.UseRedis = false; })
            .AddCache<int, string>("users", region =>
            {
                region.NullValue.Enabled = true;
                region.NullValue.Ttl = TimeSpan.FromSeconds(30);
                configureRegion?.Invoke(region);
            });

        if (loaderFactory != null)
        {
            builder.WithLoader(_ => loaderFactory(counter));
        }
        else
        {
            builder.WithLoader(_ => new TestLoader(counter));
        }

        return (services.BuildServiceProvider(), counter);
    }

    [Test]
    public async Task GetOrLoadAsync_OnMiss_ShouldLoadAndCache()
    {
        var (provider, counter) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        var first = await client.GetOrLoadAsync(1);
        var second = await client.GetOrLoadAsync(1);

        await Assert.That(first).IsEqualTo("user-1");
        await Assert.That(second).IsEqualTo("user-1");
        await Assert.That(counter.LoadCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetAsync_OnMiss_ShouldReturnDefaultWithoutLoading()
    {
        var (provider, counter) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        var value = await client.GetAsync(999);

        await Assert.That(value).IsNull();
        await Assert.That(counter.LoadCount).IsEqualTo(0);
    }

    [Test]
    public async Task PutAsync_ThenGetAsync_ShouldReadFromL1()
    {
        var (provider, counter) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await client.PutAsync(1, "written");
        var value = await client.GetAsync(1);

        await Assert.That(value).IsEqualTo("written");
        await Assert.That(counter.LoadCount).IsEqualTo(0);
    }

    [Test]
    public async Task EvictAsync_ShouldRemoveFromCache()
    {
        var (provider, _) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await client.PutAsync(1, "written");
        var removed = await client.EvictAsync(1);

        var value = await client.GetAsync(1);
        await Assert.That(removed).IsTrue();
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task GetOrLoadAsync_WhenLoaderReturnsNull_ShouldCacheNullValue()
    {
        var (provider, counter) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        var first = await client.GetOrLoadAsync(404);
        var second = await client.GetOrLoadAsync(404);

        await Assert.That(first).IsNull();
        await Assert.That(second).IsNull();
        await Assert.That(counter.LoadCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrLoadAsync_WithoutLoader_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddL2Cache(_ => { })
            .AddCache<int, string>("noload");

        using var scope = services.BuildServiceProvider().CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetOrLoadAsync(1));
    }

    [Test]
    public async Task ExistsAsync_ShouldReflectPutAndEvict()
    {
        var (provider, _) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await Assert.That(await client.ExistsAsync(1)).IsFalse();

        await client.PutAsync(1, "v");
        await Assert.That(await client.ExistsAsync(1)).IsTrue();

        await client.EvictAsync(1);
        await Assert.That(await client.ExistsAsync(1)).IsFalse();
    }

    [Test]
    public async Task BatchGetOrLoadAsync_ShouldLoadOnlyMissingKeys()
    {
        var (provider, counter) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await client.PutAsync(1, "user-1");

        var result = await client.BatchGetOrLoadAsync([1, 2, 3]);

        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[1]).IsEqualTo("user-1");
        await Assert.That(result[2]).IsEqualTo("user-2");
        await Assert.That(result[3]).IsEqualTo("user-3");
        await Assert.That(counter.LoadManyKeys).IsEquivalentTo([2, 3]);
    }

    [Test]
    public async Task BatchPutAndBatchGet_ShouldRoundTrip()
    {
        var (provider, _) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await client.BatchPutAsync(new Dictionary<int, string> { [1] = "a", [2] = "b" });

        var result = await client.BatchGetAsync([1, 2, 3]);

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[1]).IsEqualTo("a");
        await Assert.That(result[2]).IsEqualTo("b");
    }

    [Test]
    public async Task BatchEvictAsync_ShouldRemoveAllKeys()
    {
        var (provider, _) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await client.BatchPutAsync(new Dictionary<int, string> { [1] = "a", [2] = "b" });
        var removed = await client.BatchEvictAsync([1, 2]);

        var result = await client.BatchGetAsync([1, 2]);
        await Assert.That(removed).IsEqualTo(2);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task CacheName_ShouldMatchRegionName()
    {
        var (provider, _) = BuildClient();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICacheClient<int, string>>();

        await Assert.That(client.CacheName).IsEqualTo("users");
    }

    /// <summary>回源加载计数器。</summary>
    private sealed class LoadCounter
    {
        public int LoadCount;
        public List<int> LoadManyKeys { get; } = [];
    }

    private interface ITestLoader : ILoader<int, string>;

    private sealed class TestLoader(LoadCounter counter) : ITestLoader
    {
        public Task<string?> LoadAsync(int key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.LoadCount);
            return Task.FromResult<string?>(key == 404 ? null : $"user-{key}");
        }

        public Task<Dictionary<int, string>> LoadManyAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.LoadCount);
            counter.LoadManyKeys.AddRange(keys);
            return Task.FromResult(keys.ToDictionary(k => k, k => $"user-{k}"));
        }
    }
}
