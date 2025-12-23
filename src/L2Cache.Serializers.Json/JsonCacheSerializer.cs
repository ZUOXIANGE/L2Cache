using System.Text.Encodings.Web;
using System.Text.Json;
using L2Cache.Abstractions.Serialization;

namespace L2Cache.Serializers.Json;

/// <summary>
/// 基于 System.Text.Json 的缓存序列化器
/// 提供高性能的 JSON 序列化和反序列化功能
/// </summary>
public class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">JSON 序列化选项，如果为 null 则使用默认选项</param>
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

    /// <summary>
    /// 序列化器名称
    /// </summary>
    public string Name => "System.Text.Json";

    /// <summary>
    /// 序列化器版本
    /// </summary>
    public string Version => "8.0";

    /// <summary>
    /// 是否支持二进制序列化
    /// </summary>
    public bool SupportsBinary => true;

    /// <summary>
    /// 是否支持字符串序列化
    /// </summary>
    public bool SupportsString => true;

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>序列化后的字节数组</returns>
    public byte[] Serialize<T>(T value)
    {
        if (value == null)
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

    /// <summary>
    /// 序列化对象为字符串
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>序列化后的字符串</returns>
    public string SerializeToString<T>(T value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Serialize(value, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name} to string", ex);
        }
    }

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">序列化的字节数组</param>
    /// <returns>反序列化后的对象</returns>
    public T? Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// 从字符串反序列化对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">序列化的字符串</param>
    /// <returns>反序列化后的对象</returns>
    public T? DeserializeFromString<T>(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize string data to type {typeof(T).Name}", ex);
        }
    }
}
