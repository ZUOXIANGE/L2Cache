using BenchmarkDotNet.Attributes;
using L2Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace L2Cache.Benchmarks;

[MemoryDiagnoser]
public class BasicBenchmarks
{
    private ICacheClient<string, string> _cache = null!;
    private IServiceProvider _serviceProvider = null!;
    private List<string> _batchKeys = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = false; // 仅本地缓存
        })
        .AddCache<string, string>("bench_basic");

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<ICacheClient<string, string>>();

        // BatchGet 预置数据
        _batchKeys = [];
        for (int i = 0; i < 100; i++)
        {
            var key = $"batch_key_{i}";
            _batchKeys.Add(key);
            await _cache.PutAsync(key, $"data_{i}");
        }
    }

    [Benchmark]
    public async Task BasicPut()
    {
        await _cache.PutAsync($"key_{Guid.NewGuid()}", "test data");
    }

    [Benchmark]
    public async Task BasicGetMiss()
    {
        await _cache.GetAsync($"key_{Guid.NewGuid()}");
    }

    [Benchmark]
    public async Task BatchGetHit()
    {
        await _cache.BatchGetAsync(_batchKeys);
    }
}
