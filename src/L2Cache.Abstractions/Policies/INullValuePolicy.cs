namespace L2Cache.Abstractions.Policies;

/// <summary>
/// 空值缓存策略（防止缓存穿透）。
/// <para>
/// 当回源加载结果为 null 时，将空值哨兵写入缓存，避免同一 Key 的穿透请求反复打到数据源。
/// 哨兵编码由实现决定（默认为 UTF8 字符串 "@@NULL@@"），编排层负责在读取时识别。
/// </para>
/// </summary>
public interface INullValuePolicy
{
    /// <summary>是否启用空值缓存。</summary>
    bool Enabled { get; }

    /// <summary>空值缓存项的 TTL（应显著短于正常值，如 30 秒）。</summary>
    TimeSpan Ttl { get; }

    /// <summary>空值哨兵的字节载荷。</summary>
    ReadOnlyMemory<byte> NullPayload { get; }

    /// <summary>判断 L2 读取到的载荷是否为空值哨兵。</summary>
    bool IsNullPayload(ReadOnlyMemory<byte> payload);
}
