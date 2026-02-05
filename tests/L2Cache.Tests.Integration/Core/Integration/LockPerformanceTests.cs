using System.Collections.Concurrent;
using System.Diagnostics;
using L2Cache.Configuration;
using L2Cache.Extensions;
using L2Cache.Tests.Integration.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 锁性能对比测试
/// <para>对比启用锁和不启用锁对吞吐量和延迟的影响</para>
/// </summary>
public class LockPerformanceTests
{
    /// <summary>
    /// 用于性能测试的 CacheService
    /// </summary>
    public class PerfCacheService : L2CacheService<string, string>
    {
        private int _queryCount;
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

    private static async Task<PerfResult> RunGetOrLoadTestAsync(bool enableLocks, int concurrency, int dbDelayMs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = GlobalTestSetup.RedisConnectionString;
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

        // 模拟并发请求
        var tasks = new List<Task<string?>>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(() => cacheService.GetOrLoadAsync(key)));
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        return new PerfResult
        {
            LocksEnabled = enableLocks,
            TotalDurationMs = sw.ElapsedMilliseconds,
            AvgDurationMs = (double)sw.ElapsedMilliseconds / concurrency,
            Ops = concurrency / sw.Elapsed.TotalSeconds,
            SourceQueries = cacheService.QueryCount,
            SuccessCount = results.Count(r => r != null)
        };
    }

    [Test]
    public async Task Compare_Lock_Vs_NoLock_Performance()
    {
        int concurrency = 20;
        int dbDelayMs = 100;

        Console.WriteLine($"Running Performance Test (Concurrency: {concurrency}, DB Delay: {dbDelayMs}ms)");

        // 1. 无锁测试
        var noLockResult = await RunGetOrLoadTestAsync(enableLocks: false, concurrency, dbDelayMs);
        Console.WriteLine($"[No Lock] Duration: {noLockResult.TotalDurationMs}ms, Queries: {noLockResult.SourceQueries}, Success: {noLockResult.SuccessCount}");

        // 2. 有锁测试
        var withLockResult = await RunGetOrLoadTestAsync(enableLocks: true, concurrency, dbDelayMs);
        Console.WriteLine($"[With Lock] Duration: {withLockResult.TotalDurationMs}ms, Queries: {withLockResult.SourceQueries}, Success: {withLockResult.SuccessCount}");

        // Assertions
        // 开启锁应该显著减少回源次数 (理想情况是 1)
        await Assert.That(withLockResult.SourceQueries).IsLessThan(noLockResult.SourceQueries);
        await Assert.That(withLockResult.SourceQueries).IsLessThan(5); // 允许少量误差

        // 无锁情况下，回源次数应该接近并发数 (因为都有延迟)
        await Assert.That(noLockResult.SourceQueries).IsGreaterThan(1);
    }
}
