using L2Cache.Configuration;
using L2Cache.Policies;

namespace L2Cache.Tests.Unit.Policies;

/// <summary>
/// 默认过期策略测试：L2 TTL 解析与 L1 TTL 上限约束
/// </summary>
public class DefaultExpiryPolicyTests
{
    private static CacheRegionOptions CreateOptions() => new()
    {
        DefaultTtl = TimeSpan.FromMinutes(10),
        MaxL1Ttl = TimeSpan.FromMinutes(5),
        NullValue = new NullValueOptions { Enabled = true, Ttl = TimeSpan.FromSeconds(30) }
    };

    [Test]
    public async Task ResolveL2Ttl_WithRequested_ShouldUseRequested()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL2Ttl(TimeSpan.FromMinutes(1))).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task ResolveL2Ttl_WithoutRequested_ShouldUseDefaultTtl()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL2Ttl(null)).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task ResolveL2Ttl_ForNullValue_ShouldUseNullValueTtl()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL2Ttl(TimeSpan.FromMinutes(10), isNullValue: true)).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task ResolveL1Ttl_WhenL2TtlBelowMax_ShouldUseL2Ttl()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL1Ttl(TimeSpan.FromMinutes(1))).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task ResolveL1Ttl_WhenL2TtlAboveMax_ShouldCapAtMaxL1Ttl()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL1Ttl(TimeSpan.FromMinutes(30))).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task ResolveL1Ttl_WhenL2DoesNotExpire_ShouldUseMaxL1Ttl()
    {
        var policy = new DefaultExpiryPolicy(CreateOptions());

        await Assert.That(policy.ResolveL1Ttl(null)).IsEqualTo(TimeSpan.FromMinutes(5));
    }
}
