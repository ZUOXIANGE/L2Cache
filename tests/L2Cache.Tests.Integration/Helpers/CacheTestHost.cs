using L2Cache.Abstractions;
using L2Cache.Abstractions.Policies;
using L2Cache.Configuration;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace L2Cache.Tests.Integration.Helpers;

/// <summary>
/// 集成测试宿主：封装新 API（AddL2Cache / AddCache / ICacheClient）的 DI 装配。
/// <para>
/// 默认注册区域 <see cref="CacheName"/>（string -> string，JSON 序列化，带回源计数加载器）。
/// 每个 <see cref="CacheTestHost"/> 是一个独立的"节点"；跨节点同步测试创建多个实例并使用相同区域名。
/// </para>
/// </summary>
public sealed class CacheTestHost : IDisposable
{
    private bool _disposed;

    public LoadCounter Counter { get; } = new();

    public string CacheName { get; }

    public string RedisConnectionString { get; }

    private ServiceProvider? _provider;
    private IServiceScope? _scope;
    private ConnectionMultiplexer? _connection;

    public CacheTestHost(
        string redisConnectionString,
        string? cacheName = null,
        Action<L2CacheOptions>? configureGlobal = null,
        Action<CacheRegionOptions>? configureRegion = null,
        Action<IL2CacheRegionBuilder<string, string>>? configureBuilder = null)
    {
        RedisConnectionString = redisConnectionString;
        CacheName = cacheName ?? $"it_{Guid.NewGuid():N}";
        Counter = new LoadCounter();

        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddL2Cache(options =>
            {
                options.UseLocalCache = true;
                options.UseRedis = true;
                options.Redis.ConnectionString = redisConnectionString;
                configureGlobal?.Invoke(options);
            })
            .AddCache<string, string>(CacheName, region =>
            {
                region.PublishInvalidation = true;
                configureRegion?.Invoke(region);
            });

        builder.WithLoader(_ => new TestLoader(Counter));
        configureBuilder?.Invoke(builder);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        Client = _scope.ServiceProvider.GetRequiredService<ICacheClient<string, string>>();
    }

    /// <summary>区域对应的缓存客户端（Scoped）。</summary>
    public ICacheClient<string, string> Client { get; }

    /// <summary>从宿主 Scope 解析服务（如 ICacheRefreshable&lt;string&gt;）。</summary>
    public T GetService<T>() where T : notnull
        => _scope is null
            ? throw new ObjectDisposedException(nameof(CacheTestHost))
            : _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>独立的 Redis 连接（直接操作 L2，用于绕过缓存层验证底层状态）。</summary>
    public ConnectionMultiplexer Connection => _connection ??= ConnectionMultiplexer.Connect(RedisConnectionString);

    public IDatabase Db => Connection.GetDatabase();

    /// <summary>构建完整缓存 Key："{CacheName}:{key}"。</summary>
    public string FullKey(string key) => $"{CacheName}:{key}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scope?.Dispose();
        _provider?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>回源加载计数器（测试断言用）。</summary>
public sealed class LoadCounter
{
    private int _loadCount;

    /// <summary>累计回源次数。</summary>
    public int LoadCount => _loadCount;

    public List<string> LoadedKeys { get; } = [];

    public void Record(string key)
    {
        Interlocked.Increment(ref _loadCount);
        LoadedKeys.Add(key);
    }
}

/// <summary>string -> string 测试加载器：key == "null"（或以 "null_" 开头）时返回 null，否则返回 "db_{key}"。</summary>
public sealed class TestLoader(LoadCounter counter) : ILoader<string, string>
{
    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        counter.Record(key);
        return Task.FromResult(IsNullKey(key) ? null : $"db_{key}");
    }

    public Task<Dictionary<string, string>> LoadManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            counter.Record(key);
            if (!IsNullKey(key))
            {
                result[key] = $"db_{key}";
            }
        }

        return Task.FromResult(result);
    }

    private static bool IsNullKey(string key) => key == "null" || key.StartsWith("null_", StringComparison.Ordinal);
}
