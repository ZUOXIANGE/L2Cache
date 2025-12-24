using BenchmarkDotNet.Attributes;
using L2Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace L2Cache.Benchmarks;

[MemoryDiagnoser]
public class BasicBenchmarks
{
    private ICacheService<string, object> _cache = null!;
    private IServiceProvider _serviceProvider = null!;
    private List<string> _batchKeys = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = false; // Only local cache
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<ICacheService<string, object>>();

        // Setup for BatchGet
        _batchKeys = [];
        for (int i = 0; i < 100; i++)
        {
            var key = $"batch_key_{i}";
            _batchKeys.Add(key);
            await _cache.PutAsync(key, new { Index = i, Data = $"data_{i}" });
        }
    }

    [Benchmark]
    public async Task BasicPut()
    {
        await _cache.PutAsync($"key_{Guid.NewGuid()}", new { Value = "test data" });
    }

    [Benchmark]
    public async Task BasicGet_Miss()
    {
        await _cache.GetAsync($"key_{Guid.NewGuid()}");
    }

    [Benchmark]
    public async Task BatchGet_Hit()
    {
        await _cache.BatchGetAsync(_batchKeys);
    }
}

