namespace L2Cache.Abstractions;

/// <summary>
/// 多级缓存（L1 内存 + L2 分布式）的统一业务门面。
/// <para>
/// 通过 DI 获取实例：<c>ICacheClient&lt;int, ProductDto&gt;</c>，
/// 由 <c>AddL2Cache(...).AddCache&lt;int, ProductDto&gt;("products", ...)</c> 注册。
/// </para>
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
public interface ICacheClient<TKey, TValue> where TKey : notnull
{
    /// <summary>缓存区域名称（同时是 Redis Key 前缀与失效频道后缀）。</summary>
    string CacheName { get; }

    #region 单条操作

    /// <summary>查询缓存（不回源）。未命中返回 default。</summary>
    Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取或加载（Cache-Aside）。优先查缓存；未命中时通过注册的 <c>ILoader&lt;TKey,TValue&gt;</c> 回源并回填缓存。
    /// </summary>
    Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>检查缓存是否存在（L1 或 L2 任一存在即为 true）。</summary>
    Task<bool> ExistsAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>显式写入缓存（覆盖写），同时更新 L2 与 L1。</summary>
    Task PutAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>仅当缓存不存在时写入（NX 模式，仅作用于 L2）。</summary>
    Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>移除指定 Key 的缓存（L1 + L2），并广播失效消息。</summary>
    Task<bool> EvictAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>强制回源加载最新数据并写回缓存。需要已注册 Loader。</summary>
    Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    #endregion

    #region 批量操作

    /// <summary>批量查询缓存（不回源）。结果仅包含命中的非空值。</summary>
    Task<Dictionary<TKey, TValue>> BatchGetAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取或加载。未命中的 Key 通过 <c>ILoader.LoadManyAsync</c> 批量回源并回填缓存。
    /// 已命中"空值"的 Key 不会重复回源。
    /// </summary>
    Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(IReadOnlyList<TKey> keys, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>批量写入缓存（Pipeline 优化）。</summary>
    Task BatchPutAsync(IReadOnlyDictionary<TKey, TValue> data, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>批量移除缓存（L1 + L2），并广播失效消息。</summary>
    Task<long> BatchEvictAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// 定义支持后台刷新的缓存客户端。
/// <para>由 <c>AddCache(...).WithBackgroundRefresh()</c> 启用，后台服务据此调度刷新。</para>
/// </summary>
public interface ICacheRefreshable<TKey> where TKey : notnull
{
    /// <summary>
    /// 刷新指定 Key 的缓存：优先使用 L2 最新值，否则回源加载，并写回缓存。
    /// </summary>
    Task RefreshKeyAsync(TKey key, CancellationToken cancellationToken = default);
}
