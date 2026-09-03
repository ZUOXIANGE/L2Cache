using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using L2Cache.Abstractions.Invalidation;

namespace L2Cache.Benchmarks;

/// <summary>
/// 序列化方式对比：反射（传 options）vs JsonSerializerContext source-gen vs 缓存 JsonTypeInfo。
/// <para>运行：dotnet run -c Release -- --filter *SerializationBenchmarks* --job short</para>
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonTypeInfo<SampleModel> CachedModelTypeInfo = CreateTypeInfo<SampleModel>();
    private static readonly JsonTypeInfo<string> CachedStringTypeInfo = CreateTypeInfo<string>();

    // GetTypeInfo 不会为未初始化的 options 自动补默认反射解析器，需显式设置
    private static JsonTypeInfo<T> CreateTypeInfo<T>()
    {
        if (CacheOptions.TypeInfoResolver is null)
        {
            CacheOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        }

        return (JsonTypeInfo<T>)CacheOptions.GetTypeInfo(typeof(T));
    }

    private readonly InvalidationMessage _message = new("bench_cache_region", "user:42:profile", 123456789);
    private readonly string _stringValue = "缓存测试字符串 benchmark-value-42";
    private readonly SampleModel _model = new()
    {
        Id = 42,
        Name = "缓存测试用户",
        Email = "user@example.com",
        Score = 98.5,
        Tags = ["cache", "l2", "redis"],
        CreatedAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc)
    };

    // 预序列化在字段初始化器中完成：BDN 的 OverheadJitting 会在 GlobalSetup 之前调用一次基准方法，
    // 依赖 GlobalSetup 的字段在该阶段尚未初始化，字段初始化器则始终安全
    private readonly byte[] _messageJsonBytes =
        "{\"cacheName\":\"bench_cache_region\",\"key\":\"user:42:profile\",\"version\":123456789}"u8.ToArray();
    private readonly byte[] _modelJsonBytes = JsonSerializer.SerializeToUtf8Bytes(new SampleModel
    {
        Id = 42,
        Name = "缓存测试用户",
        Email = "user@example.com",
        Score = 98.5,
        Tags = ["cache", "l2", "redis"],
        CreatedAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc)
    }, CacheOptions);

    // ---------- 失效消息（固定类型，可 source-gen） ----------

    [Benchmark(Baseline = true)]
    public byte[] Invalidation_Reflection()
        => JsonSerializer.SerializeToUtf8Bytes(_message, WebOptions);

    [Benchmark]
    public byte[] Invalidation_SourceGen()
        => JsonSerializer.SerializeToUtf8Bytes(_message, BenchJsonContext.Default.InvalidationMessage);

    [Benchmark]
    public InvalidationMessage Invalidation_Reflection_Deserialize()
        => JsonSerializer.Deserialize<InvalidationMessage>(_messageJsonBytes, WebOptions);

    [Benchmark]
    public InvalidationMessage Invalidation_SourceGen_Deserialize()
        => JsonSerializer.Deserialize(_messageJsonBytes, BenchJsonContext.Default.InvalidationMessage);

    // ---------- 缓存值（任意泛型类型，无法 source-gen，对比 TypeInfo 缓存） ----------

    [Benchmark]
    public byte[] Model_Reflection()
        => JsonSerializer.SerializeToUtf8Bytes(_model, CacheOptions);

    [Benchmark]
    public byte[] Model_TypeInfoCache()
        => JsonSerializer.SerializeToUtf8Bytes(_model, CachedModelTypeInfo);

    [Benchmark]
    public byte[] String_Reflection()
        => JsonSerializer.SerializeToUtf8Bytes(_stringValue, CacheOptions);

    [Benchmark]
    public byte[] String_TypeInfoCache()
        => JsonSerializer.SerializeToUtf8Bytes(_stringValue, CachedStringTypeInfo);

    [Benchmark]
    public SampleModel? Model_Reflection_Deserialize()
        => JsonSerializer.Deserialize<SampleModel>(_modelJsonBytes, CacheOptions);

    [Benchmark]
    public SampleModel? Model_TypeInfoCache_Deserialize()
        => JsonSerializer.Deserialize(_modelJsonBytes, CachedModelTypeInfo);
}

/// <summary>基准用缓存值模型。</summary>
public sealed class SampleModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public double Score { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

/// <summary>基准内部使用的源生成上下文（与 L2Cache 内部实现一致）。</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(InvalidationMessage))]
internal sealed partial class BenchJsonContext : JsonSerializerContext;
