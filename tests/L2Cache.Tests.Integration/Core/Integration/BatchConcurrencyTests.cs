using L2Cache.Abstractions.Policies;
using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 批量操作并发测试
/// <para>批量获取路径不持有锁，并发调用可能多次回源（击穿），但每个调用方拿到的结果必须一致。</para>
/// </summary>
public class BatchConcurrencyTests
{
    /// <summary>带延迟的批量加载器：模拟 DB 延迟以放大并发窗口，并统计批量回源次数。</summary>
    private sealed class DelayedBatchLoader : ILoader<string, string>
    {
        private int _loadManyCount;

        public int LoadManyCount => _loadManyCount;

        public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return $"val_{key}";
        }

        public async Task<Dictionary<string, string>> LoadManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loadManyCount);
            await Task.Delay(50, cancellationToken);
            return keys.ToDictionary(k => k, k => $"val_{k}");
        }
    }

    /// <summary>
    /// 测试：并发 BatchGetOrLoadAsync 结果必须一致（允许回源风暴）
    /// </summary>
    [Test]
    public async Task BatchGetOrLoadAsync_ConcurrentCalls_ShouldHaveConsistentResults_ButMayCauseStampede()
    {
        // Arrange：通过 configureBuilder 追加注册带延迟计数的 Loader（同类型后注册的生效）
        var loader = new DelayedBatchLoader();
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureBuilder: builder => builder.WithLoader(_ => loader));

        var keys = Enumerable.Range(0, 10).Select(i => $"batch_key_{i}").ToList();
        int concurrentClients = 10;

        // Act
        var tasks = new List<Task<Dictionary<string, string>>>();
        for (int i = 0; i < concurrentClients; i++)
        {
            tasks.Add(Task.Run(() => host.Client.BatchGetOrLoadAsync(keys)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert：结果一致性（批量路径无锁，可能多次回源，但每个调用方结果必须完整一致）
        foreach (var result in results)
        {
            await Assert.That(result).Count().IsEqualTo(keys.Count);
            foreach (var key in keys)
            {
                await Assert.That(result.ContainsKey(key)).IsTrue();
                await Assert.That(result[key]).IsEqualTo($"val_{key}");
            }
        }

        // Assert：至少触发过一次批量回源
        await Assert.That(loader.LoadManyCount).IsGreaterThan(0);
    }
}
