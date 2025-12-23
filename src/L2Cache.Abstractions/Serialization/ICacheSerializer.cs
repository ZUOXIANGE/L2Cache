namespace L2Cache.Abstractions.Serialization;

/// <summary>
/// 缓存序列化器接口
/// 定义缓存数据的序列化和反序列化方法
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>序列化后的字节数组</returns>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// 序列化对象为字符串
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>序列化后的字符串</returns>
    string SerializeToString<T>(T value);

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">序列化的字节数组</param>
    /// <returns>反序列化后的对象</returns>
    T? Deserialize<T>(byte[] data);

    /// <summary>
    /// 从字符串反序列化对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">序列化的字符串</param>
    /// <returns>反序列化后的对象</returns>
    T? DeserializeFromString<T>(string data);

    /// <summary>
    /// 获取序列化器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取序列化器版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 是否支持二进制序列化
    /// </summary>
    bool SupportsBinary { get; }

    /// <summary>
    /// 是否支持字符串序列化
    /// </summary>
    bool SupportsString { get; }
}