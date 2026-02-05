using L2Cache.Serializers.MemoryPack;
using MemoryPack;

namespace L2Cache.Tests.Unit.Serializers;

/// <summary>
/// MemoryPack 缓存序列化器测试
/// 测试基于 MemoryPack 的高性能二进制序列化实现
/// </summary>
public partial class MemoryPackSerializerTests
{
    private readonly MemoryPackCacheSerializer _serializer;
    private readonly TestData _testData;

    public MemoryPackSerializerTests()
    {
        _serializer = new MemoryPackCacheSerializer();
        _testData = new TestData
        {
            Id = 123,
            Name = "Test Name",
            Value = 45.67,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 构造函数应成功创建实例
    /// </summary>
    [Test]
    public async Task Constructor_ShouldCreateInstance()
    {
        // Act (执行)
        var serializer = new MemoryPackCacheSerializer();

        // Assert (断言)
        await Assert.That(serializer).IsNotNull();
    }

    /// <summary>
    /// 测试序列化有效对象应返回字节数组
    /// </summary>
    [Test]
    public async Task Serialize_WithValidObject_ShouldReturnByteArray()
    {
        // Act (执行)
        var result = _serializer.Serialize(_testData);

        // Assert (断言)
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsNotEmpty();
    }

    /// <summary>
    /// 测试序列化空对象应返回空数组
    /// </summary>
    [Test]
    public async Task Serialize_WithNullObject_ShouldReturnEmptyArray()
    {
        // Act (执行)
        var result = _serializer.Serialize<TestData>(null!);

        // Assert (断言)
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    /// <summary>
    /// 测试反序列化有效字节数组应返回对象
    /// </summary>
    [Test]
    public async Task Deserialize_WithValidByteArray_ShouldReturnObject()
    {
        // Arrange (准备)
        var serializedData = _serializer.Serialize(_testData);

        // Act (执行)
        var result = _serializer.Deserialize<TestData>(serializedData);

        // Assert (断言)
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(_testData.Id);
        await Assert.That(result.Name).IsEqualTo(_testData.Name);
        await Assert.That(result.Value).IsEqualTo(_testData.Value);
        await Assert.That(result.IsActive).IsEqualTo(_testData.IsActive);
        // TUnit 简化范围检查
        await Assert.That(result.CreatedAt).IsGreaterThan(_testData.CreatedAt.AddSeconds(-1));
        await Assert.That(result.CreatedAt).IsLessThan(_testData.CreatedAt.AddSeconds(1));
    }

    /// <summary>
    /// 测试反序列化空字节数组(null)应返回默认值
    /// </summary>
    [Test]
    public async Task Deserialize_WithNullByteArray_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.Deserialize<TestData>(null!);

        // Assert (断言)
        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// 测试反序列化空字节数组(empty)应返回默认值
    /// </summary>
    [Test]
    public async Task Deserialize_WithEmptyByteArray_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.Deserialize<TestData>(Array.Empty<byte>());

        // Assert (断言)
        await Assert.That(result).IsNull();
    }

    [MemoryPackable]
    private sealed partial class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
