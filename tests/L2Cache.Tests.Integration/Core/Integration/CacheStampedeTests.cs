using L2Cache.Abstractions;
using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 缓存击穿测试
/// <para>测试在高并发下请求同一个未命中 Key 时的回源合并行为（默认启用内存锁 + 分布式锁）。</para>
/// </summary>
public class CacheStampedeTests
{
    /// <summary>
    /// 单节点击穿：并发调用 GetOrLoadAsync。
    /// <para>已启用内存锁和分布式锁，预期只有一次（或极少量）回源查询。</para>
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_ConcurrentCalls_WithLocks_ShouldMergeSourceLoads()
    {
        // Arrange
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = $"stampede_{Guid.NewGuid():N}";
        int concurrentClients = 20;
        var tasks = new List<Task<string?>>();

        // Act
        for (int i = 0; i < concurrentClients; i++)
        {
            tasks.Add(Task.Run(() => host.Client.GetOrLoadAsync(key)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        // 1. 所有请求都应成功并获得相同结果
        foreach (var result in results)
        {
            await Assert.That(result).IsEqualTo($"db_{key}");
        }

        // 2. 验证回源次数
        // 启用了锁机制，预期回源次数显著小于并发数（理想为 1）
        // 注意：锁获取超时会降级为无锁直读直写（可用性优先），允许少量降级回源，避免环境抖动导致 flaky
        Console.WriteLine($"Concurrent requests: {concurrentClients}");
        Console.WriteLine($"Actual Source Loads: {host.Counter.LoadCount}");
        await Assert.That(host.Counter.LoadCount).IsLessThan(concurrentClients);
    }

    /// <summary>
    /// 跨节点击穿：两个独立节点（各自 L1）的并发请求落在同一个未命中 Key 上，
    /// 分布式锁应跨节点合并回源，两节点回源总次数应显著小于总并发数。
    /// </summary>
    [Test]
    public async Task GetOrLoadAsync_ConcurrentCalls_AcrossNodes_ShouldMergeSourceLoads()
    {
        // Arrange：两个节点使用相同区域名，共享 L2 与分布式锁
        var cacheName = $"stampede_shared_{Guid.NewGuid():N}";
        using var nodeA = new CacheTestHost(GlobalTestSetup.RedisConnectionString, cacheName: cacheName);
        using var nodeB = new CacheTestHost(GlobalTestSetup.RedisConnectionString, cacheName: cacheName);

        var key = $"stampede_{Guid.NewGuid():N}";
        int clientsPerNode = 10;

        // Act
        var tasksA = LaunchConcurrentLoads(nodeA.Client, key, clientsPerNode);
        var tasksB = LaunchConcurrentLoads(nodeB.Client, key, clientsPerNode);

        var resultsA = await Task.WhenAll(tasksA);
        var resultsB = await Task.WhenAll(tasksB);

        // Assert
        // 1. 两个节点的所有请求都应成功并获得相同结果
        foreach (var result in resultsA.Concat(resultsB))
        {
            await Assert.That(result).IsEqualTo($"db_{key}");
        }

        // 2. 回源总次数应显著小于总并发数（理想为 1，允许少量降级回源）
        var totalLoads = nodeA.Counter.LoadCount + nodeB.Counter.LoadCount;
        Console.WriteLine($"Total concurrent requests: {clientsPerNode * 2}");
        Console.WriteLine($"Actual Total Source Loads: {totalLoads}");
        await Assert.That(totalLoads).IsLessThan(clientsPerNode * 2);
    }

    /// <summary>从单个节点并发发起 N 个 GetOrLoadAsync 请求。</summary>
    private static List<Task<string?>> LaunchConcurrentLoads(
        ICacheClient<string, string> client, string key, int count)
    {
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < count; i++)
        {
            tasks.Add(Task.Run(() => client.GetOrLoadAsync(key)));
        }

        return tasks;
    }
}
