using System.Text.Encodings.Web;
using System.Text.Json;
using L2Cache.Abstractions.Serialization;

namespace L2Cache.Serializers.Json;

/// <summary>
/// 基于 System.Text.Json 的缓存序列化器。
/// <para>输出 UTF-8 字节流，与 L2 存储的字节语义直接对接。</para>
/// </summary>
public class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">JSON 序列化选项，如果为 null 则使用默认选项。</param>
    public JsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 支持中文字符
        };
    }

    /// <inheritdoc />
    public string Name => "System.Text.Json";

    /// <inheritdoc />
    public byte[] Serialize<T>(T? value)
    {
        if (value is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name}", ex);
        }
    }

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(data.Span, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name}", ex);
        }
    }
}
