namespace L2Cache.Abstractions;

/// <summary>
/// 缓存刷新策略接口
/// 用于确定特定缓存Key的刷新间隔
/// </summary>
/// <typeparam name="TKey">缓存Key类型</typeparam>
/// <typeparam name="TValue">缓存Value类型</typeparam>
public interface ICacheRefreshPolicy<TKey, TValue> where TKey : notnull
{
    /// <summary>
    /// 获取指定Key的刷新间隔
    /// </summary>
    /// <param name="key">缓存Key</param>
    /// <returns>刷新间隔，如果返回null则使用默认间隔</returns>
    TimeSpan? GetRefreshInterval(TKey key);
}
