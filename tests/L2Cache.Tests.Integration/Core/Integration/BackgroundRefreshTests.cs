using System.Text.Json;
using L2Cache.Abstractions;
using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 后台刷新功能测试
/// <para>
/// 新架构下，后台刷新由 CacheRefreshBackgroundService（IHostedService）按 Interval 轮询驱动；
/// 集成测试中通过手动调用 <see cref="ICacheRefreshable{TKey}.RefreshKeyAsync"/> 验证单次刷新逻辑，
/// 避免依赖真实定时轮询，保证确定性。
/// </para>
/// </summary>
public class BackgroundRefreshTests
{
    /// <summary>
    /// 测试：手动触发刷新时，应优先采用 L2 最新值回填 L1（模拟其他节点更新了数据）
    /// </summary>
    [Test]
    public async Task RefreshKeyAsync_Should_Pull_Latest_L2_Value_Into_L1()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureBuilder: b => b.WithBackgroundRefresh(o => o.Interval = TimeSpan.FromMilliseconds(100)));

        var client = host.Client;
        var refreshable = host.GetService<ICacheRefreshable<string>>();
        var key = "refresh_l2_key";

        // 首次回源，使 L1 / L2 均有值并进入刷新跟踪
        var initial = await client.GetOrLoadAsync(key);
        await Assert.That(initial).IsEqualTo($"db_{key}");

        // 直接更新 L2（模拟其他节点写入了新值），绕过本节点 L1
        await host.Db.StringSetAsync(host.FullKey(key), JsonSerializer.Serialize("v2"));

        // Act (执行)：触发一次后台刷新
        await refreshable.RefreshKeyAsync(key);

        // Assert (断言)：L1 已被 L2 最新值覆盖
        var refreshed = await client.GetAsync(key);
        await Assert.That(refreshed).IsEqualTo("v2");
    }

    /// <summary>
    /// 测试：L2 无值时，刷新应回源加载并回填缓存
    /// </summary>
    [Test]
    public async Task RefreshKeyAsync_Should_Load_From_Source_When_L2_Missing()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureBuilder: b => b.WithBackgroundRefresh(o => o.Interval = TimeSpan.FromMilliseconds(100)));

        var client = host.Client;
        var refreshable = host.GetService<ICacheRefreshable<string>>();
        var key = "refresh_source_key";

        // 首次回源（计入 1 次加载）
        _ = await client.GetOrLoadAsync(key);
        var loadCountAfterInitial = host.Counter.LoadCount;
        await Assert.That(loadCountAfterInitial).IsEqualTo(1);

        // 外部删除 L2（模拟缓存数据过期或被清除），此时 L1 中仍保留旧值
        await host.Db.KeyDeleteAsync(host.FullKey(key));

        // Act (执行)：触发一次后台刷新
        await refreshable.RefreshKeyAsync(key);

        // Assert (断言)：刷新时应回源一次并回填
        var refreshed = await client.GetAsync(key);
        await Assert.That(refreshed).IsEqualTo($"db_{key}");
        await Assert.That(host.Counter.LoadCount).IsEqualTo(loadCountAfterInitial + 1);
    }

    /// <summary>
    /// 测试：L1 中已不存在的 Key 刷新时应直接停止跟踪，不触发回源
    /// </summary>
    [Test]
    public async Task RefreshKeyAsync_Should_StopTracking_When_Key_Not_In_L1()
    {
        // Arrange (准备)
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureBuilder: b => b.WithBackgroundRefresh(o => o.Interval = TimeSpan.FromMilliseconds(100)));

        var client = host.Client;
        var refreshable = host.GetService<ICacheRefreshable<string>>();
        var key = "refresh_evicted_key";

        // 首次回源后清除缓存（L1 / L2 均为空）
        _ = await client.GetOrLoadAsync(key);
        await client.EvictAsync(key);
        var loadCountAfterEvict = host.Counter.LoadCount;

        // Act (执行)：L1 已无值，刷新应跳过
        await refreshable.RefreshKeyAsync(key);

        // Assert (断言)：未发生回源，且缓存仍为空
        await Assert.That(host.Counter.LoadCount).IsEqualTo(loadCountAfterEvict);
        var value = await client.GetAsync(key);
        await Assert.That(value).IsNull();
    }
}
