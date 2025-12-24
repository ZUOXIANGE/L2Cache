using BenchmarkDotNet.Attributes;
using L2Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace L2Cache.Benchmarks;

[MemoryDiagnoser]
public class AdvancedBenchmarks
{
    private ICacheService<string, object> _cache = null!;
    private IServiceProvider _serviceProvider = null!;
    private byte[] _largeData = null!;
    private string _largeObjectKey = null!;
    private List<string> _hitTestKeys = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = "localhost:6379";
            options.Redis.Database = 0;
            options.Telemetry.EnableMetrics = true;
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<ICacheService<string, object>>();

        // Setup Large Data
        _largeData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(_largeData);
        _largeObjectKey = "large_object_fixed";
        await _cache.PutAsync(_largeObjectKey, _largeData);

        // Setup Hit Test Keys
        _hitTestKeys = [];
        for (int i = 0; i < 1000; i++)
        {
            var key = $"hit_test_{i}";
            _hitTestKeys.Add(key);
            await _cache.PutAsync(key, new { Index = i });
        }
    }

    [Benchmark]
    public async Task LargeObjectPut()
    {
        await _cache.PutAsync($"large_{Guid.NewGuid()}", _largeData);
    }

    [Benchmark]
    public async Task LargeObjectGet_Hit()
    {
        await _cache.GetAsync(_largeObjectKey);
    }

    [Benchmark]
    public async Task CacheHitTest()
    {
        // Simulate random access to existing keys
        var randomKey = _hitTestKeys[new Random().Next(_hitTestKeys.Count)];
        await _cache.GetAsync(randomKey);
    }

    // Optional: Expose metrics if needed, but BenchmarkDotNet handles timing/memory.
    // We could have a cleanup method if we wanted to check metrics at the end, 
    // but BenchmarkDotNet runs methods many times.
}

