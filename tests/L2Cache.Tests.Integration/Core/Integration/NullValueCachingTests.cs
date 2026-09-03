using L2Cache.Tests.Integration.Helpers;

namespace L2Cache.Tests.Integration.Core.Integration;

/// <summary>
/// 空值缓存测试（防穿透）
/// <para>
/// 测试回源得到空值时的缓存行为：
/// 启用空值缓存时，首次回源得到 null 后向 L2 写入 "@@NULL@@" 哨兵，后续调用不再重复回源；
/// 关闭空值缓存时，空值不写缓存，每次调用都会回源。
/// </para>
/// </summary>
public class NullValueCachingTests
{
    [Test]
    public async Task GetOrLoadAsync_Should_Cache_Null_When_Enabled()
    {
        // Arrange（准备）：启用空值缓存
        using var host = new CacheTestHost(
            GlobalTestSetup.RedisConnectionString,
            configureRegion: region =>
            {
                region.NullValue.Enabled = true;
                region.NullValue.Ttl = TimeSpan.FromSeconds(5);
            });

        var key = "null_key_1";
        var fullKey = host.FullKey(key);

        // Act 1（执行）：首次调用（未命中 -> 回源得到 null -> 缓存空值哨兵）
        var result1 = await host.Client.GetOrLoadAsync(key);

        // Assert 1（断言）
        await Assert.That(result1).IsNull();
        await Assert.That(host.Counter.LoadCount).IsEqualTo(1);

        // 验证 Redis 中写入了 "@@NULL@@" 空值哨兵
        var redisVal = await host.Db.StringGetAsync(fullKey);
        await Assert.That(redisVal.HasValue).IsTrue();
        await Assert.That(redisVal.ToString()).IsEqualTo("@@NULL@@");

        // Act 2：第二次调用（命中空值缓存 -> 直接返回 null，不重复回源）
        var result2 = await host.Client.GetOrLoadAsync(key);

        // Assert 2
        await Assert.That(result2).IsNull();
        await Assert.That(host.Counter.LoadCount).IsEqualTo(1); // 回源计数不增加
    }

    [Test]
    public async Task GetOrLoadAsync_Should_Not_Cache_Null_When_Disabled()
    {
        // Arrange（准备）：关闭空值缓存（默认行为）
        using var host = new CacheTestHost(GlobalTestSetup.RedisConnectionString);

        var key = "null_key_2";
        var fullKey = host.FullKey(key);

        // Act 1（执行）：首次调用（未命中 -> 回源得到 null，不写缓存）
        var result1 = await host.Client.GetOrLoadAsync(key);

        // Assert 1（断言）
        await Assert.That(result1).IsNull();
        await Assert.That(host.Counter.LoadCount).IsEqualTo(1);

        // 验证 Redis 中没有写入任何值
        var redisVal = await host.Db.StringGetAsync(fullKey);
        await Assert.That(redisVal.HasValue).IsFalse();

        // Act 2：第二次调用（缓存未命中 -> 再次回源）
        var result2 = await host.Client.GetOrLoadAsync(key);

        // Assert 2
        await Assert.That(result2).IsNull();
        await Assert.That(host.Counter.LoadCount).IsEqualTo(2); // 应再次回源
    }
}
