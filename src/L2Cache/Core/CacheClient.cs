using L2Cache.Abstractions;
using L2Cache.Abstractions.Policies;
using L2Cache.Configuration;
using L2Cache.Internal;

namespace L2Cache.Core;

/// <summary>
/// <see cref="ICacheClient{TKey,TValue}"/> 的默认实现：薄门面，仅做 Key 构建与描述符解析，
/// 全部逻辑转发给 <see cref="CacheOrchestrator"/>。
/// <para>Scoped 注册；当区域启用后台刷新时同时实现 <see cref="ICacheRefreshable{TKey}"/>。</para>
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
internal sealed class CacheClient<TKey, TValue> : ICacheClient<TKey, TValue>, ICacheRefreshable<TKey> where TKey : notnull
{
    private readonly CacheDescriptor<TKey, TValue> _descriptor;
    private readonly CacheOrchestrator _orchestrator;
    private readonly ILoader<TKey, TValue>? _loader;
    private readonly CacheKeyTracker<TKey, TValue>? _tracker;

    public CacheClient(
        CacheDescriptor<TKey, TValue> descriptor,
        CacheOrchestrator orchestrator,
        ILoader<TKey, TValue>? loader = null,
        CacheKeyTracker<TKey, TValue>? tracker = null)
    {
        _descriptor = descriptor;
        _orchestrator = orchestrator;
        _loader = loader;
        _tracker = tracker;
    }

    public string CacheName => _descriptor.CacheName;

    private ILoader<TKey, TValue> Loader =>
        _loader ?? throw new InvalidOperationException(
            $"缓存区域 '{CacheName}' 未配置回源加载器（AddCache(...).WithLoader）。");

    #region 单条操作

    public Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default)
        => WrapGet(_orchestrator.GetAsync(_descriptor, key, cancellationToken));

    public Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => WrapGet(_orchestrator.GetOrLoadAsync(_descriptor, Loader, key, expiry, cancellationToken));

    public Task<bool> ExistsAsync(TKey key, CancellationToken cancellationToken = default)
        => _orchestrator.ExistsAsync(_descriptor, key, cancellationToken);

    public Task PutAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => _orchestrator.PutAsync(_descriptor, key, value, expiry, cancellationToken);

    public Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => _orchestrator.PutIfAbsentAsync(_descriptor, key, value, expiry, cancellationToken);

    public Task<bool> EvictAsync(TKey key, CancellationToken cancellationToken = default)
        => _orchestrator.EvictAsync(_descriptor, key, cancellationToken);

    public Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => _orchestrator.ReloadAsync(_descriptor, Loader, key, expiry, cancellationToken);

    #endregion

    #region 批量操作

    public Task<Dictionary<TKey, TValue>> BatchGetAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default)
        => _orchestrator.BatchGetAsync(_descriptor, keys, cancellationToken);

    public Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(IReadOnlyList<TKey> keys, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => _orchestrator.BatchGetOrLoadAsync(_descriptor, Loader, keys, expiry, cancellationToken);

    public Task BatchPutAsync(IReadOnlyDictionary<TKey, TValue> data, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => _orchestrator.BatchPutAsync(_descriptor, data, expiry, cancellationToken);

    public Task<long> BatchEvictAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default)
        => _orchestrator.BatchEvictAsync(_descriptor, keys, cancellationToken);

    #endregion

    #region 后台刷新（ICacheRefreshable）

    /// <inheritdoc />
    public async Task RefreshKeyAsync(TKey key, CancellationToken cancellationToken = default)
    {
        if (_tracker == null)
        {
            throw new InvalidOperationException(
                $"缓存区域 '{CacheName}' 未启用后台刷新（AddCache(...).WithBackgroundRefresh），无法执行 RefreshKeyAsync。");
        }

        // 1. L1 中已不存在（过期/淘汰/被失效清除）则停止跟踪
        if (!_orchestrator.ExistsInL1(_descriptor, key))
        {
            _tracker.Untrack(key);
            return;
        }

        var loader = Loader;

        // 2. 优先采用 L2 最新值（多节点场景下比自己回源更新）
        var value = await _orchestrator.GetL2ValueAsync(_descriptor, key, cancellationToken).ConfigureAwait(false);

        // 3. L2 无值（未启用 Redis / 已过期 / 为空值哨兵）时回源
        if (value == null)
        {
            value = await loader.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        }

        // 4. 写回缓存或淘汰
        if (value != null)
        {
            await _orchestrator.PutAsync(_descriptor, key, value, null, cancellationToken).ConfigureAwait(false);
            _tracker.UpdateNextRefresh(key);
        }
        else if (_descriptor.NullValue.Enabled)
        {
            await _orchestrator.PutAsync(_descriptor, key, default!, null, cancellationToken).ConfigureAwait(false);
            _tracker.UpdateNextRefresh(key);
        }
        else
        {
            // 数据源已物理删除：淘汰并停止跟踪
            await _orchestrator.EvictAsync(_descriptor, key, cancellationToken).ConfigureAwait(false);
            _tracker.Untrack(key);
        }
    }

    #endregion

    /// <summary>编排器返回 CacheValue，ICacheClient 对外契约返回 TValue?（FoundNull/NotFound 均为 default）。</summary>
    private static async Task<TValue?> WrapGet(Task<CacheValue<TValue>> task)
    {
        var result = await task.ConfigureAwait(false);
        return result.Status == CacheStatus.Found ? result.Value : default;
    }
}
