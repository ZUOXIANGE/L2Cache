using L2Cache.Abstractions.Policies;

namespace L2Cache.Tests.Unit.Policies;

/// <summary>
/// 默认 Key 构建策略测试
/// </summary>
public class DefaultKeyBuilderTests
{
    [Test]
    public async Task Build_WithStringKey_ShouldReturnSameString()
    {
        var builder = new DefaultKeyBuilder<string>();

        await Assert.That(builder.Build("user:1")).IsEqualTo("user:1");
    }

    [Test]
    public async Task Build_WithPrimitiveKey_ShouldReturnToString()
    {
        var intBuilder = new DefaultKeyBuilder<int>();
        var longBuilder = new DefaultKeyBuilder<long>();
        var guidBuilder = new DefaultKeyBuilder<Guid>();
        var enumBuilder = new DefaultKeyBuilder<TestEnum>();

        await Assert.That(intBuilder.Build(42)).IsEqualTo("42");
        await Assert.That(longBuilder.Build(100L)).IsEqualTo("100");
        await Assert.That(enumBuilder.Build(TestEnum.Two)).IsEqualTo("Two");
        await Assert.That(guidBuilder.Build(Guid.Empty)).IsEqualTo(Guid.Empty.ToString());
    }

    [Test]
    public async Task Build_WithComplexType_ShouldThrow()
    {
        var builder = new DefaultKeyBuilder<TestKey>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(builder.Build(new TestKey("a", 1))));
    }

    private enum TestEnum
    {
        One,
        Two
    }

    private sealed record TestKey(string Name, int Id);
}
