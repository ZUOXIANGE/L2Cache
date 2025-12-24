namespace L2Cache.Abstractions;

/// <summary>
/// 业务维度的缓存接口
/// <para>
/// 作用：
/// 1）标准化业务系统中对缓存的增删改查操作，避免缓存操作与业务逻辑强耦合在一起。
/// 2）作为业务系统中的一个缓存层，对业务逻辑和缓存组件而言，起到承上启下的作用，可简化业务开发，降低开发复杂度。
/// </para>
/// </summary>
/// <typeparam name="TKey">表示缓存key，可以是单个字段（如 string, int），也可以是一个自定义对象（DTO）。</typeparam>
/// <typeparam name="TValue">表示返回的缓存数据类型。</typeparam>
public interface ICacheService<TKey, TValue> where TKey : notnull
{
    #region 基础配置与工具 (Infrastructure & Tools)

    /// <summary>
    /// 获取缓存实例的名称。
    /// <para>通常用于区分不同的业务缓存区域或命名空间。</para>
    /// </summary>
    /// <returns>缓存名称。</returns>
    string GetCacheName();

    #endregion

    #region 单条缓存操作 (Single Item Operations)

    /// <summary>
    /// 获取缓存数据（仅查询缓存）。
    /// <para>尝试从缓存中获取数据，如果不存在则直接返回 null，不会触发回源加载。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>缓存中的数据。如果不存在，返回 default(TValue) 或 null。</returns>
    Task<TValue?> GetAsync(TKey key);

    /// <summary>
    /// 获取或加载缓存（Cache-Aside 模式）。
    /// <para>优先从缓存获取；如果缓存不存在，则调用 <see cref="QueryDataAsync"/> 加载数据并回填到缓存中。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>缓存数据或新加载的数据。</returns>
    Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null);

    /// <summary>
    /// 检查缓存是否存在。
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>如果缓存存在返回 true，否则返回 false。</returns>
    Task<bool> ExistsAsync(TKey key);

    /// <summary>
    /// 显式设置指定 Key 的缓存项。
    /// <para>无论缓存是否存在，都会覆盖原有值。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">要缓存的数据。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>写入的缓存数据。</returns>
    Task<TValue> PutAsync(TKey key, TValue value, TimeSpan? expiry = null);

    /// <summary>
    /// 仅当缓存不存在时，才设置缓存项。
    /// <para>通常用于避免并发重复计算或简单的分布式锁场景。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">要缓存的数据。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>如果写入成功（即之前不存在）返回 true，否则返回 false。</returns>
    Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null);

    /// <summary>
    /// 更新业务数据并清除缓存。
    /// <para>这是一个组合操作：先调用 <see cref="UpdateDataAsync"/> 更新数据源，然后移除对应的缓存项，以保证数据一致性。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="value">新的业务数据。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task UpdateAsync(TKey key, TValue value);

    /// <summary>
    /// 重新加载缓存。
    /// <para>强制调用 <see cref="QueryDataAsync"/> 从数据源加载最新数据，并更新到缓存中。</para>
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>重新加载后的最新数据。</returns>
    Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null);

    /// <summary>
    /// 淘汰（移除）指定 Key 的缓存项。
    /// </summary>
    /// <param name="key">业务 Key。</param>
    /// <returns>如果移除成功返回 true，否则返回 false。</returns>
    Task<bool> EvictAsync(TKey key);

    #endregion

    #region 批量缓存操作 (Batch Operations)

    /// <summary>
    /// 批量获取缓存数据（仅查询缓存）。
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <returns>包含命中的业务 Key 和对应数据的字典。</returns>
    Task<Dictionary<TKey, TValue>> BatchGetAsync(List<TKey> keyList);

    /// <summary>
    /// 批量获取或加载缓存。
    /// <para>优先批量查询缓存；对于未命中的 Key，调用 <see cref="QueryDataListAsync"/> 批量回源加载，并回填缓存。</para>
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <param name="expiry">缓存过期时间。如果为 null，则使用默认配置。</param>
    /// <returns>包含所有请求 Key 和对应数据的字典。</returns>
    Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(List<TKey> keyList, TimeSpan? expiry = null);

    /// <summary>
    /// 批量淘汰（移除）缓存项。
    /// </summary>
    /// <param name="keyList">业务 Key 列表。</param>
    /// <returns>成功移除的缓存项数量。</returns>
    Task<long> BatchEvictAsync(List<TKey> keyList);

    #endregion

}
