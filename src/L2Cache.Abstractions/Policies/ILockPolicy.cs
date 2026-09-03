namespace L2Cache.Abstractions.Policies;

/// <summary>
/// 缓存锁句柄。Dispose/释放后锁被释放。
/// </summary>
public interface ICacheLockHandle : IAsyncDisposable
{
    /// <summary>是否成功获取到锁。false 表示降级（未获取到但允许继续执行）。</summary>
    bool Acquired { get; }
}

/// <summary>
/// 锁策略。封装"内存锁 / 分布式锁"的获取与释放，用于防止缓存击穿和并发写入冲突。
/// <para>
/// 契约：
/// 1. 返回 null 表示"未获取到但允许降级直读/直写"（可用性优先）；
/// 2. 返回的句柄 <see cref="ICacheLockHandle.Acquired"/> 为 false 时同样表示降级；
/// 3. 实现不应抛出锁获取超时异常（取消令牌触发除外）。
/// </para>
/// </summary>
public interface ILockPolicy
{
    /// <summary>
    /// 尝试获取锁。
    /// </summary>
    /// <param name="resourceKey">资源 Key（含区域前缀的完整缓存 Key）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>锁句柄；null 表示策略未配置或不可用（降级）。</returns>
    ValueTask<ICacheLockHandle?> AcquireAsync(string resourceKey, CancellationToken cancellationToken = default);
}
