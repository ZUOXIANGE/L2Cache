using L2Cache.Configuration;
using L2Cache.Policies;

namespace L2Cache.Tests.Unit.Policies;

/// <summary>
/// 哨兵空值策略测试：空值载荷识别与配置透传
/// </summary>
public class SentinelNullValuePolicyTests
{
    [Test]
    public async Task NullPayload_ShouldBeDefaultSentinel()
    {
        var policy = new SentinelNullValuePolicy(new NullValueOptions());

        await Assert.That(policy.NullPayload.ToArray()).IsEquivalentTo("@@NULL@@"u8.ToArray());
    }

    [Test]
    public async Task IsNullPayload_WithSentinelBytes_ShouldReturnTrue()
    {
        var policy = new SentinelNullValuePolicy(new NullValueOptions());

        await Assert.That(policy.IsNullPayload("@@NULL@@"u8.ToArray())).IsTrue();
    }

    [Test]
    public async Task IsNullPayload_WithNormalValue_ShouldReturnFalse()
    {
        var policy = new SentinelNullValuePolicy(new NullValueOptions());

        await Assert.That(policy.IsNullPayload("hello"u8.ToArray())).IsFalse();
    }

    [Test]
    public async Task EnabledAndTtl_ShouldReflectOptions()
    {
        var options = new NullValueOptions { Enabled = true, Ttl = TimeSpan.FromSeconds(15) };
        var policy = new SentinelNullValuePolicy(options);

        await Assert.That(policy.Enabled).IsTrue();
        await Assert.That(policy.Ttl).IsEqualTo(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task IsNullPayload_WithCustomPayload_ShouldMatchOnlyCustomPayload()
    {
        var policy = new SentinelNullValuePolicy(new NullValueOptions(), "<nil>"u8.ToArray());

        await Assert.That(policy.IsNullPayload("<nil>"u8.ToArray())).IsTrue();
        await Assert.That(policy.IsNullPayload("@@NULL@@"u8.ToArray())).IsFalse();
    }
}
