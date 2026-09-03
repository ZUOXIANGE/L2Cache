using BenchmarkDotNet.Attributes;
using L2Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace L2Cache.Benchmarks;

[MemoryDiagnoser]
public class AdvancedBenchmarks : IDisposable
{
    private ICacheClient<string, byte[]> _cache = null!;
    private IServiceProvider _serviceProvider = null!;
    private byte[] _largeData = null!;
    private string _largeObjectKey = null!;
    private List<string> _hitTestKeys = null!;

    private RedisContainer _redisContainer = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        // 启动 Redis 容器
        _redisContainer = new RedisContainer();
        _redisContainer.Start();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _redisContainer.ConnectionString;
            options.Redis.Database = 0;
            options.Telemetry.EnableMetrics = true;
        })
        .AddCache<string, byte[]>("bench_advanced");

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<ICacheClient<string, byte[]>>();

        // 设置大数据对象（1MB）
        _largeData = new byte[1024 * 1024];
        new Random(42).NextBytes(_largeData);
        _largeObjectKey = "large_object_fixed";
        await _cache.PutAsync(_largeObjectKey, _largeData);

        // 设置命中测试用的 Keys
        _hitTestKeys = [];
        for (int i = 0; i < 1000; i++)
        {
            var key = $"hit_test_{i}";
            _hitTestKeys.Add(key);
            await _cache.PutAsync(key, new byte[64]);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _redisContainer?.Dispose();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async Task LargeObjectPut()
    {
        await _cache.PutAsync($"large_{Guid.NewGuid()}", _largeData);
    }

    [Benchmark]
    public async Task LargeObjectGetHit()
    {
        await _cache.GetAsync(_largeObjectKey);
    }

    [Benchmark]
    public async Task CacheHitTest()
    {
        // Simulate random access to existing keys
        var randomKey = _hitTestKeys[Random.Shared.Next(_hitTestKeys.Count)];
        await _cache.GetAsync(randomKey);
    }
}
