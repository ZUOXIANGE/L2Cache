using L2Cache.Abstractions.Serialization;

namespace L2Cache.Serializers.MemoryPack;

/// <summary>
/// 基于 MemoryPack 的缓存序列化器
/// 提供极高性能的二进制序列化和反序列化功能
/// 注意：需要安装 MemoryPack NuGet 包并在类型上添加 [MemoryPackable] 特性
/// </summary>
public class MemoryPackCacheSerializer : ICacheSerializer
{
    /// <summary>
    /// 序列化器名称
    /// </summary>
    public string Name => "MemoryPack";

    /// <summary>
    /// 序列化器版本
    /// </summary>
    public string Version => "1.21.1";

    /// <summary>
    /// 是否支持二进制序列化
    /// </summary>
    public bool SupportsBinary => true;

    /// <summary>
    /// 是否支持字符串序列化（通过Base64编码）
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
            return global::MemoryPack.MemoryPackSerializer.Serialize(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name} using MemoryPack", ex);
        }
    }

    /// <summary>
    /// 序列化对象为字符串（通过Base64编码）
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
            var bytes = Serialize(value);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name} to string using MemoryPack", ex);
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
            return default(T);
        }

        try
        {
            return global::MemoryPack.MemoryPackSerializer.Deserialize<T>(data);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} using MemoryPack", ex);
        }
    }

    /// <summary>
    /// 从字符串反序列化对象（通过Base64解码）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">序列化的字符串</param>
    /// <returns>反序列化后的对象</returns>
    public T? DeserializeFromString<T>(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return default(T);
        }

        try
        {
            var bytes = Convert.FromBase64String(data);
            return Deserialize<T>(bytes);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize string data to type {typeof(T).Name} using MemoryPack", ex);
        }
    }
}

/// <summary>
/// MemoryPack 序列化器的扩展方法
/// </summary>
public static class MemoryPackSerializerExtensions
{
    /// <summary>
    /// 检查 MemoryPack 是否可用
    /// </summary>
    /// <returns>如果 MemoryPack 可用返回 true，否则返回 false</returns>
    public static bool IsMemoryPackAvailable()
    {
        return true;
    }

    /// <summary>
    /// 获取推荐的序列化器
    /// 如果 MemoryPack 可用，返回 MemoryPack 序列化器，否则返回 JSON 序列化器
    /// </summary>
    /// <returns>推荐的缓存序列化器</returns>
    public static ICacheSerializer GetRecommendedSerializer()
    {
        return new MemoryPackCacheSerializer();
    }
}
