using System.Threading.Tasks;

namespace L2Cache.Abstractions;

/// <summary>
/// 定义支持后台刷新的缓存服务接口。
/// <para>
/// 实现此接口的缓存服务可以被 <see cref="L2Cache.Background.CacheRefreshBackgroundService{TKey, TValue}"/> 自动调度刷新。
/// </para>
/// </summary>
/// <typeparam name="TKey">缓存键的类型。</typeparam>
public interface ICacheRefreshable<TKey> where TKey : notnull
{
    /// <summary>
    /// 刷新指定 Key 的缓存。
    /// <para>通常涉及检查缓存有效性、从数据源重新加载数据并更新缓存。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RefreshKeyAsync(TKey key);
}
