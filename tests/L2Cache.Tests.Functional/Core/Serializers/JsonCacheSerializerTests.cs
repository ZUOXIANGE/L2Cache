using System.Text;
using FluentAssertions;
using L2Cache.Serializers.Json;

namespace L2Cache.Tests.Functional.Serializers;

/// <summary>
/// JSON 缓存序列化器测试
/// 测试基于 System.Text.Json 的序列化实现
/// </summary>
public class JsonCacheSerializerTests
{
    private readonly JsonCacheSerializer _serializer;
    private readonly TestData _testData;

    public JsonCacheSerializerTests()
    {
        _serializer = new JsonCacheSerializer();
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
    [Fact]
    public void Constructor_ShouldCreateInstance()
    {
        // Act (执行)
        var serializer = new JsonCacheSerializer();

        // Assert (断言)
        serializer.Should().NotBeNull();
    }

    /// <summary>
    /// 测试序列化有效对象应返回字节数组
    /// </summary>
    [Fact]
    public void Serialize_WithValidObject_ShouldReturnByteArray()
    {
        // Act (执行)
        var result = _serializer.Serialize(_testData);

        // Assert (断言)
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试序列化空对象应返回空数组
    /// </summary>
    [Fact]
    public void Serialize_WithNullObject_ShouldReturnEmptyArray()
    {
        // Act (执行)
        var result = _serializer.Serialize<TestData>(null!);

        // Assert (断言)
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试反序列化有效字节数组应返回对象
    /// </summary>
    [Fact]
    public void Deserialize_WithValidByteArray_ShouldReturnObject()
    {
        // Arrange (准备)
        var serializedData = _serializer.Serialize(_testData);

        // Act (执行)
        var result = _serializer.Deserialize<TestData>(serializedData);

        // Assert (断言)
        result.Should().NotBeNull();
        result!.Id.Should().Be(_testData.Id);
        result.Name.Should().Be(_testData.Name);
        result.Value.Should().Be(_testData.Value);
        result.IsActive.Should().Be(_testData.IsActive);
        result.CreatedAt.Should().BeCloseTo(_testData.CreatedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试反序列化空字节数组(null)应返回默认值
    /// </summary>
    [Fact]
    public void Deserialize_WithNullByteArray_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.Deserialize<TestData>(null!);

        // Assert (断言)
        result.Should().BeNull();
    }

    /// <summary>
    /// 测试反序列化空字节数组(empty)应返回默认值
    /// </summary>
    [Fact]
    public void Deserialize_WithEmptyByteArray_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.Deserialize<TestData>([]);

        // Assert (断言)
        result.Should().BeNull();
    }

    /// <summary>
    /// 测试反序列化无效字节数组应抛出异常
    /// </summary>
    [Fact]
    public void Deserialize_WithInvalidByteArray_ShouldThrowException()
    {
        // Arrange (准备)
        var invalidData = Encoding.UTF8.GetBytes("invalid json");

        // Act & Assert (执行 & 断言)
        Assert.Throws<InvalidOperationException>(() => 
            _serializer.Deserialize<TestData>(invalidData));
    }

    /// <summary>
    /// 测试序列化为字符串
    /// </summary>
    [Fact]
    public void SerializeToString_WithValidObject_ShouldReturnJsonString()
    {
        // Act (执行)
        var result = _serializer.SerializeToString(_testData);

        // Assert (断言)
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain(_testData.Name);
        result.Should().Contain(_testData.Id.ToString());
    }

    /// <summary>
    /// 测试序列化空对象为字符串应返回空字符串
    /// </summary>
    [Fact]
    public void SerializeToString_WithNullObject_ShouldReturnEmptyString()
    {
        // Act (执行)
        var result = _serializer.SerializeToString<TestData>(null!);

        // Assert (断言)
        result.Should().Be(string.Empty);
    }

    /// <summary>
    /// 测试从字符串反序列化
    /// </summary>
    [Fact]
    public void DeserializeFromString_WithValidJsonString_ShouldReturnObject()
    {
        // Arrange (准备)
        var jsonString = _serializer.SerializeToString(_testData);

        // Act (执行)
        var result = _serializer.DeserializeFromString<TestData>(jsonString);

        // Assert (断言)
        result.Should().NotBeNull();
        result!.Id.Should().Be(_testData.Id);
        result.Name.Should().Be(_testData.Name);
        result.Value.Should().Be(_testData.Value);
        result.IsActive.Should().Be(_testData.IsActive);
        result.CreatedAt.Should().BeCloseTo(_testData.CreatedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试从空字符串(null)反序列化应返回默认值
    /// </summary>
    [Fact]
    public void DeserializeFromString_WithNullString_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.DeserializeFromString<TestData>(null!);

        // Assert (断言)
        result.Should().BeNull();
    }

    /// <summary>
    /// 测试从空字符串(empty)反序列化应返回默认值
    /// </summary>
    [Fact]
    public void DeserializeFromString_WithEmptyString_ShouldReturnDefault()
    {
        // Act (执行)
        var result = _serializer.DeserializeFromString<TestData>(string.Empty);

        // Assert (断言)
        result.Should().BeNull();
    }

    /// <summary>
    /// 测试从无效JSON字符串反序列化应抛出异常
    /// </summary>
    [Fact]
    public void DeserializeFromString_WithInvalidJsonString_ShouldThrowException()
    {
        // Arrange (准备)
        var invalidJson = "invalid json";

        // Act & Assert (执行 & 断言)
        Assert.Throws<InvalidOperationException>(() => 
            _serializer.DeserializeFromString<TestData>(invalidJson));
    }

    /// <summary>
    /// 测试字节数组序列化和反序列化往返
    /// </summary>
    [Fact]
    public void SerializeDeserialize_RoundTrip_ShouldPreserveData()
    {
        // Act (执行)
        var serialized = _serializer.Serialize(_testData);
        var deserialized = _serializer.Deserialize<TestData>(serialized);

        // Assert (断言)
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(_testData.Id);
        deserialized.Name.Should().Be(_testData.Name);
        deserialized.Value.Should().Be(_testData.Value);
        deserialized.IsActive.Should().Be(_testData.IsActive);
        deserialized.CreatedAt.Should().BeCloseTo(_testData.CreatedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试字符串序列化和反序列化往返
    /// </summary>
    [Fact]
    public void SerializeToStringDeserializeFromString_RoundTrip_ShouldPreserveData()
    {
        // Act (执行)
        var serialized = _serializer.SerializeToString(_testData);
        var deserialized = _serializer.DeserializeFromString<TestData>(serialized);

        // Assert (断言)
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(_testData.Id);
        deserialized.Name.Should().Be(_testData.Name);
        deserialized.Value.Should().Be(_testData.Value);
        deserialized.IsActive.Should().Be(_testData.IsActive);
        deserialized.CreatedAt.Should().BeCloseTo(_testData.CreatedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试不同整数值
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Serialize_WithDifferentIntegerValues_ShouldWork(int value)
    {
        // Arrange (准备)
        var data = new TestData { Id = value };

        // Act (执行)
        var serialized = _serializer.Serialize(data);
        var deserialized = _serializer.Deserialize<TestData>(serialized);

        // Assert (断言)
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(value);
    }

    /// <summary>
    /// 测试不同字符串值 (包括特殊字符和Unicode)
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Simple text")]
    [InlineData("Text with special characters: !@#$%^&*()")]
    [InlineData("Unicode text: 你好世界")]
    public void Serialize_WithDifferentStringValues_ShouldWork(string value)
    {
        // Arrange (准备)
        var data = new TestData { Name = value };

        // Act (执行)
        var serialized = _serializer.Serialize(data);
        var deserialized = _serializer.Deserialize<TestData>(serialized);

        // Assert (断言)
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be(value);
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
