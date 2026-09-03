using L2Cache.Abstractions.Serialization;

namespace L2Cache.Serializers.MemoryPack;

/// <summary>
/// 基于 MemoryPack 的缓存序列化器。
/// <para>极高性能的二进制序列化。注意：需要在类型上添加 [MemoryPackable] 特性。</para>
/// </summary>
public class MemoryPackCacheSerializer : ICacheSerializer
{
    /// <inheritdoc />
    public string Name => "MemoryPack";

    /// <inheritdoc />
    public byte[] Serialize<T>(T? value)
    {
        if (value is null)
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

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return default;
        }

        try
        {
            return global::MemoryPack.MemoryPackSerializer.Deserialize<T>(data.Span);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} using MemoryPack", ex);
        }
    }
}
