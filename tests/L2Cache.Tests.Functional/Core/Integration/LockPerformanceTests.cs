using System.Collections.Concurrent;
using System.Diagnostics;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Functional.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace L2Cache.Tests.Functional.Core.Integration;

/// <summary>
/// 锁性能对比测试
/// <para>对比启用锁和不启用锁对吞吐量和延迟的影响</para>
/// </summary>
[Collection("Shared Test Collection")]
public class LockPerformanceTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;
    private readonly RedisTestFixture _fixture;

    public LockPerformanceTests(RedisTestFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    /// <summary>
    /// 用于性能测试的 CacheService
    /// </summary>
    public class PerfCacheService : L2CacheService<string, string>
    {
        private int _queryCount = 0;
        public int QueryCount => _queryCount;
        public int DbDelayMs { get; set; } = 50;

        public PerfCacheService(
            IServiceProvider sp,
            IOptions<L2CacheOptions> opts,
            ILogger<L2CacheService<string, string>> logger)
            : base(sp, opts, logger)
        {
        }

        public override string GetCacheName() => "perf_test";
        public override string BuildCacheKey(string key) => key;

        public void Reset()
        {
            _queryCount = 0;
        }

        protected override async Task<string?> QueryDataAsync(string key)
        {
            Interlocked.Increment(ref _queryCount);
            if (DbDelayMs > 0)
            {
                await Task.Delay(DbDelayMs);
            }
            return $"val_{key}";
        }
    }

    private struct PerfResult
    {
        public bool LocksEnabled;
        public long TotalDurationMs;
        public double AvgDurationMs;
        public double Ops;
        public int SourceQueries;
        public int SuccessCount;
    }

    private async Task<PerfResult> RunGetOrLoadTestAsync(bool enableLocks, int concurrency, int dbDelayMs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.Lock.EnabledMemoryLock = enableLocks;
            options.Lock.EnabledDistributedLock = enableLocks;
            // 减少锁等待超时，防止在性能测试中过久阻塞
            options.Lock.LockTimeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<PerfCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<PerfCacheService>();
        
        cacheService.DbDelayMs = dbDelayMs;
        cacheService.Reset();

        // 预热连接
        await cacheService.GetAsync("warmup");

        var key = $"perf_get_{Guid.NewGuid()}";
        var tasks = new List<Task<long>>();
        var swTotal = Stopwatch.StartNew();

        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                await cacheService.GetOrLoadAsync(key);
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }));
        }

        var durations = await Task.WhenAll(tasks);
        swTotal.Stop();

        return new PerfResult
        {
            LocksEnabled = enableLocks,
            TotalDurationMs = swTotal.ElapsedMilliseconds,
            AvgDurationMs = durations.Average(),
            Ops = concurrency / swTotal.Elapsed.TotalSeconds,
            SourceQueries = cacheService.QueryCount,
            SuccessCount = durations.Length
        };
    }
    
    private async Task<PerfResult> RunPutTestAsync(bool enableLocks, int concurrency, int iterationsPerThread)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.Lock.EnabledMemoryLock = enableLocks;
            options.Lock.EnabledDistributedLock = enableLocks;
        });
        services.AddSingleton<PerfCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<PerfCacheService>();

        // 预热
        await cacheService.PutAsync("warmup", "val");

        var key = $"perf_put_{Guid.NewGuid()}";
        var tasks = new List<Task<long>>();
        var swTotal = Stopwatch.StartNew();

        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                for (int j = 0; j < iterationsPerThread; j++)
                {
                    await cacheService.PutAsync(key, $"val_{j}");
                }
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }));
        }

        var durations = await Task.WhenAll(tasks);
        swTotal.Stop();

        long totalOps = concurrency * iterationsPerThread;

        return new PerfResult
        {
            LocksEnabled = enableLocks,
            TotalDurationMs = swTotal.ElapsedMilliseconds,
            AvgDurationMs = durations.Average() / iterationsPerThread, // 平均每次 Put 的耗时
            Ops = totalOps / swTotal.Elapsed.TotalSeconds,
            SourceQueries = 0,
            SuccessCount = (int)totalOps
        };
    }

    [Fact]
    public async Task Compare_GetOrLoad_Performance()
    {
        int concurrency = 50;
        int dbDelayMs = 50;

        _output.WriteLine($"=== GetOrLoadAsync Performance Comparison (Concurrency: {concurrency}, DB Delay: {dbDelayMs}ms) ===");

        // 1. Run Without Locks
        var noLockResult = await RunGetOrLoadTestAsync(false, concurrency, dbDelayMs);
        _output.WriteLine($"[No Lock]   Total: {noLockResult.TotalDurationMs}ms, Avg Latency: {noLockResult.AvgDurationMs:F2}ms, Source Queries: {noLockResult.SourceQueries}");

        // Clean up Redis key for next run
        var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        await redis.GetDatabase().ExecuteAsync("FLUSHDB");

        // 2. Run With Locks
        var withLockResult = await RunGetOrLoadTestAsync(true, concurrency, dbDelayMs);
        _output.WriteLine($"[With Lock] Total: {withLockResult.TotalDurationMs}ms, Avg Latency: {withLockResult.AvgDurationMs:F2}ms, Source Queries: {withLockResult.SourceQueries}");

        _output.WriteLine("--------------------------------------------------");
        _output.WriteLine($"DB Load Reduction: {(1 - (double)withLockResult.SourceQueries / noLockResult.SourceQueries) * 100:F1}%");
        
        // Assertions
        // 开启锁后，回源次数应显著减少 (理想情况是 1，无锁情况可能是 concurrency)
        Assert.True(withLockResult.SourceQueries < noLockResult.SourceQueries, "开启锁应减少回源次数");
        Assert.Equal(1, withLockResult.SourceQueries); // 严格验证击穿保护
    }

    [Fact]
    public async Task Compare_Put_Performance()
    {
        int concurrency = 10;
        int iterations = 50;
        
        _output.WriteLine($"=== PutAsync Performance Comparison (Concurrency: {concurrency}, Iterations: {iterations}) ===");

        // 1. Run Without Locks
        var noLockResult = await RunPutTestAsync(false, false, concurrency, iterations);
        _output.WriteLine($"[No Lock]      Total: {noLockResult.TotalDurationMs}ms, Avg Latency: {noLockResult.AvgDurationMs:F2}ms, OPS: {noLockResult.Ops:F2}");

        await FlushDbAsync();

        // 2. Run With Memory Lock Only
        var memoryLockResult = await RunPutTestAsync(true, false, concurrency, iterations);
        _output.WriteLine($"[Memory Lock]  Total: {memoryLockResult.TotalDurationMs}ms, Avg Latency: {memoryLockResult.AvgDurationMs:F2}ms, OPS: {memoryLockResult.Ops:F2}");

        await FlushDbAsync();

        // 3. Run With Full Locks (Memory + Distributed)
        var fullLockResult = await RunPutTestAsync(true, true, concurrency, iterations);
        _output.WriteLine($"[Full Lock]    Total: {fullLockResult.TotalDurationMs}ms, Avg Latency: {fullLockResult.AvgDurationMs:F2}ms, OPS: {fullLockResult.Ops:F2}");

        _output.WriteLine("--------------------------------------------------");
        _output.WriteLine($"Latency Increase (Mem vs No):  {(memoryLockResult.AvgDurationMs - noLockResult.AvgDurationMs) / noLockResult.AvgDurationMs * 100:F1}%");
        _output.WriteLine($"Latency Increase (Full vs No): {(fullLockResult.AvgDurationMs - noLockResult.AvgDurationMs) / noLockResult.AvgDurationMs * 100:F1}%");

        // Assertions
        Assert.Equal(concurrency * iterations, fullLockResult.SuccessCount);
    }

    private async Task FlushDbAsync()
    {
        var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        await redis.GetDatabase().ExecuteAsync("FLUSHDB");
    }

    private async Task<PerfResult> RunPutTestAsync(bool enableMemoryLock, bool enableDistributedLock, int concurrency, int iterationsPerThread)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = _fixture.ConnectionString;
            options.Lock.EnabledMemoryLock = enableMemoryLock;
            options.Lock.EnabledDistributedLock = enableDistributedLock;
        });
        services.AddSingleton<PerfCacheService>();
        var sp = services.BuildServiceProvider();
        var cacheService = sp.GetRequiredService<PerfCacheService>();

        // 预热
        await cacheService.PutAsync("warmup", "val");

        var key = $"perf_put_{Guid.NewGuid()}";
        var tasks = new List<Task<long>>();
        var swTotal = Stopwatch.StartNew();

        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                for (int j = 0; j < iterationsPerThread; j++)
                {
                    await cacheService.PutAsync(key, $"val_{j}");
                }
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }));
        }

        var durations = await Task.WhenAll(tasks);
        swTotal.Stop();

        long totalOps = concurrency * iterationsPerThread;

        return new PerfResult
        {
            LocksEnabled = enableMemoryLock || enableDistributedLock,
            TotalDurationMs = swTotal.ElapsedMilliseconds,
            AvgDurationMs = durations.Average() / iterationsPerThread, // 平均每次 Put 的耗时
            Ops = totalOps / swTotal.Elapsed.TotalSeconds,
            SourceQueries = 0,
            SuccessCount = (int)totalOps
        };
    }
}
