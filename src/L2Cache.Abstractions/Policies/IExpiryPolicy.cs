namespace L2Cache.Abstractions.Policies;

/// <summary>
/// 过期策略。决定 L2 / L1 两级缓存各自的过期时间。
/// </summary>
public interface IExpiryPolicy
{
    /// <summary>
    /// 解析 L2（分布式缓存）的 TTL。
    /// </summary>
    /// <param name="requested">调用方显式指定的 TTL（可为 null 表示未指定）。</param>
    /// <param name="isNullValue">是否为空值缓存项（空值通常使用更短的 TTL）。</param>
    /// <returns>解析后的 TTL；null 表示不过期。</returns>
    TimeSpan? ResolveL2Ttl(TimeSpan? requested, bool isNullValue = false);

    /// <summary>
    /// 解析 L1（本地缓存）的 TTL。
    /// <para>L1 TTL 应显著短于 L2 TTL，以缩小多节点间数据不一致的时间窗口。</para>
    /// </summary>
    /// <param name="l2Ttl">对应的 L2 TTL（可为 null 表示 L2 不过期）。</param>
    /// <returns>解析后的 L1 TTL。</returns>
    TimeSpan ResolveL1Ttl(TimeSpan? l2Ttl);
}
