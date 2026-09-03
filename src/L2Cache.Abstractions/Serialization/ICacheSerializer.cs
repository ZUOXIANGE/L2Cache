namespace L2Cache.Abstractions.Serialization;

/// <summary>
/// 缓存序列化器接口。
/// <para>
/// 序列化发生在编排层（L2 存储之前），序列化器只负责对象与字节流之间的转换。
/// </para>
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// 序列化器名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 序列化对象为字节数组。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="value">要序列化的对象。</param>
    /// <returns>序列化后的字节数组。</returns>
    byte[] Serialize<T>(T? value);

    /// <summary>
    /// 从字节流反序列化对象。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="data">序列化后的字节流。</param>
    /// <returns>反序列化后的对象。</returns>
    T? Deserialize<T>(ReadOnlyMemory<byte> data);
}
