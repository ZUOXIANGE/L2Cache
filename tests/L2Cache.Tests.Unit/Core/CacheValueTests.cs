using L2Cache.Core;

namespace L2Cache.Tests.Unit.Core;

/// <summary>
/// CacheValue 工厂语义测试：显式区分有效值 / 空值 / 未命中
/// </summary>
public class CacheValueTests
{
    [Test]
    public async Task Found_ShouldHaveFoundStatusAndValue()
    {
        var value = CacheValue.Found("abc");

        await Assert.That(value.Status).IsEqualTo(CacheStatus.Found);
        await Assert.That(value.Value).IsEqualTo("abc");
        await Assert.That(value.IsFound).IsTrue();
        await Assert.That(value.IsFoundNull).IsFalse();
        await Assert.That(value.IsNotFound).IsFalse();
    }

    [Test]
    public async Task FoundNull_ShouldHaveNullValue()
    {
        var value = CacheValue.FoundNull<string>();

        await Assert.That(value.Status).IsEqualTo(CacheStatus.FoundNull);
        await Assert.That(value.Value).IsNull();
        await Assert.That(value.IsFoundNull).IsTrue();
        await Assert.That(value.IsFound).IsFalse();
    }

    [Test]
    public async Task NotFound_ShouldHaveNotFoundStatus()
    {
        var value = CacheValue.NotFound<string>();

        await Assert.That(value.Status).IsEqualTo(CacheStatus.NotFound);
        await Assert.That(value.Value).IsNull();
        await Assert.That(value.IsNotFound).IsTrue();
        await Assert.That(value.IsFound).IsFalse();
    }
}
