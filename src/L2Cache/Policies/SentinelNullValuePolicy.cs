using System.Text;
using L2Cache.Abstractions.Policies;
using L2Cache.Configuration;

namespace L2Cache.Policies;

/// <summary>
/// 哨兵空值策略：以固定字节序列（默认 UTF8 "@@NULL@@"）标记空值，兼容 L2 中的历史数据格式。
/// </summary>
public sealed class SentinelNullValuePolicy : INullValuePolicy
{
    internal static readonly ReadOnlyMemory<byte> DefaultPayload = "@@NULL@@"u8.ToArray();

    private readonly NullValueOptions _options;
    private readonly ReadOnlyMemory<byte> _payload;

    public SentinelNullValuePolicy(NullValueOptions options)
        : this(options, DefaultPayload)
    {
    }

    public SentinelNullValuePolicy(NullValueOptions options, ReadOnlyMemory<byte> payload)
    {
        _options = options;
        _payload = payload;
    }

    public bool Enabled => _options.Enabled;

    public TimeSpan Ttl => _options.Ttl;

    public ReadOnlyMemory<byte> NullPayload => _payload;

    public bool IsNullPayload(ReadOnlyMemory<byte> payload)
    {
        return payload.Span.SequenceEqual(_payload.Span);
    }
}
