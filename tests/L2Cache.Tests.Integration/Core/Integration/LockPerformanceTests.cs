using L2Cache.Abstractions.Policies;
using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 锁机制验证测试
/// <para>
/// 原性能基准式对比测试已压缩为功能验证（不做耗时断言，避免 CI 抖动）：
/// 仅验证锁机制的回源合并效果——关闭锁时并发放大回源，开启锁时回源被合并。
/// </para>
/// </summary>
public class LockPerformanceTests
{
    /// <summary>带回源延迟的测试加载器：模拟数据库延迟，放大并发竞争窗口。</summary>
    private sealed class DelayedLoader(LoadCounter counter, int delayMs) : ILoader<string, string>
    {
        public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            counter.Record(key);
            await Task.Delay(delayMs, cancellationToken);
            return $"db_{key}";
        }

        public async Task<Dictionary<string, string>> LoadManyAsync(
            IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keys)
            {
                counter.Record(key);
                await Task.Delay(delayMs, cancellationToken);
                result[key] = $"db_{key}";
            }

            return result;
        }
    }

    /// <summary>一轮并发压测的结果（Key、各请求结果、回源次数）。</summary>
    private sealed record ConcurrentRunResult(string Key, string?[] Results, int LoadCount);

    /// <summary>
    /// 用栅栏保证所有并发请求同时发起后执行并发 GetOrLoadAsync。
    /// <para>
    /// 通过 configureBuilder 注册带回源延迟的加载器覆盖宿主默认 TestLoader（后注册者生效）。
    /// </para>
    /// </summary>
    private static async Task<ConcurrentRunResult> RunConcurrentGetOrLoadAsync(
        string cacheName, bool enableLocks, int concurrency, int dbDelayMs)
    {
        var counter = new LoadCounter();
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            cacheName: cacheName,
            configureRegion: region =>
            {
                region.Lock.EnabledMemoryLock = enableLocks;
                region.Lock.EnabledDistributedLock = enableLocks;
                // 减少锁等待超时，超时后降级为无锁直读直写（可用性优先）
                region.Lock.LockTimeout = TimeSpan.FromSeconds(5);
            },
            configureBuilder: builder => builder.WithLoader(_ => new DelayedLoader(counter, dbDelayMs)));

        var key = $"perf_get_{Guid.NewGuid():N}";

        // 用栅栏确保所有请求同时发起，充分暴露并发竞争窗口
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;
                return await host.Client.GetOrLoadAsync(key);
            }));
        }

        await Task.Delay(200); // 等待全部任务就绪
        startGate.SetResult();

        var results = await Task.WhenAll(tasks);
        return new ConcurrentRunResult(key, results, counter.LoadCount);
    }

    /// <summary>
    /// 对比开启/关闭锁时的并发回源次数（仅功能验证，不做耗时断言）：
    /// 开启锁应显著减少回源次数（理想为 1，允许少量降级回源）；关闭锁时并发未合并回源。
    /// </summary>
    [Test]
    public async Task Compare_Lock_Vs_NoLock_Concurrent_Source_Loads()
    {
        int concurrency = 20;
        int dbDelayMs = 100;

        // 1. 无锁：并发请求几乎同时未命中并各自回源
        var noLockResult = await RunConcurrentGetOrLoadAsync(
            $"perf_nolock_{Guid.NewGuid():N}", enableLocks: false, concurrency, dbDelayMs);
        Console.WriteLine($"[No Lock] Source loads: {noLockResult.LoadCount}/{concurrency}");

        // 2. 有锁：锁合并回源，只有一次（或极少量）真正回源
        var withLockResult = await RunConcurrentGetOrLoadAsync(
            $"perf_lock_{Guid.NewGuid():N}", enableLocks: true, concurrency, dbDelayMs);
        Console.WriteLine($"[With Lock] Source loads: {withLockResult.LoadCount}/{concurrency}");

        // 断言 1：所有请求都应成功并获得与自身 Key 匹配的回源结果
        foreach (var result in noLockResult.Results)
        {
            await Assert.That(result).IsEqualTo($"db_{noLockResult.Key}");
        }

        foreach (var result in withLockResult.Results)
        {
            await Assert.That(result).IsEqualTo($"db_{withLockResult.Key}");
        }

        // 断言 2：无锁情况下回源未合并（回源次数大于 1）
        await Assert.That(noLockResult.LoadCount).IsGreaterThan(1);

        // 断言 3：开启锁应显著减少回源次数（理想为 1，允许少量降级回源）
        await Assert.That(withLockResult.LoadCount).IsLessThan(concurrency);

        // 断言 4：开启锁后的回源次数应少于无锁
        await Assert.That(withLockResult.LoadCount).IsLessThan(noLockResult.LoadCount);
    }
}
