using L2Cache.Abstractions.Policies;
using L2Cache.Configuration;

namespace L2Cache.Policies;

/// <summary>
/// 默认过期策略。
/// <para>
/// L2 TTL：显式指定优先，其次区域默认 TTL，空值使用空值 TTL。
/// L1 TTL：min(L2 TTL, MaxL1Ttl)；L2 不过期时为 MaxL1Ttl。
/// </para>
/// </summary>
public sealed class DefaultExpiryPolicy : IExpiryPolicy
{
    private readonly CacheRegionOptions _options;

    public DefaultExpiryPolicy(CacheRegionOptions options)
    {
        _options = options;
    }

    public TimeSpan? ResolveL2Ttl(TimeSpan? requested, bool isNullValue = false)
    {
        if (isNullValue)
        {
            return _options.NullValue.Ttl;
        }

        return requested ?? _options.DefaultTtl;
    }

    public TimeSpan ResolveL1Ttl(TimeSpan? l2Ttl)
    {
        if (l2Ttl.HasValue && l2Ttl.Value < _options.MaxL1Ttl)
        {
            return l2Ttl.Value;
        }

        return _options.MaxL1Ttl;
    }
}
