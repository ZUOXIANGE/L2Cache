namespace L2Cache.Abstractions.Policies;

/// <summary>
/// 回源加载器。当 L1/L2 均未命中时，由编排层调用以从数据源（数据库、远程服务等）加载数据。
/// <para>
/// 注册方式：通过 <c>AddCache(...).WithLoader&lt;TLoader&gt;()</c> 或委托工厂注册到 DI。
/// </para>
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
/// <typeparam name="TValue">缓存值类型。</typeparam>
public interface ILoader<TKey, TValue> where TKey : notnull
{
    /// <summary>加载单个 Key 的数据。数据不存在时返回 default。</summary>
    Task<TValue?> LoadAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>批量加载多个 Key 的数据。结果中不包含数据源不存在的 Key。</summary>
    Task<Dictionary<TKey, TValue>> LoadManyAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);
}

/// <summary>
/// 加载器基类：默认将批量加载实现为逐 Key 调用 <see cref="LoadAsync"/>。
/// <para>支持真正批量回源（如 IN 查询）时，请覆写 <see cref="LoadManyAsync"/>。</para>
/// </summary>
public abstract class LoaderBase<TKey, TValue> : ILoader<TKey, TValue> where TKey : notnull
{
    /// <inheritdoc />
    public abstract Task<TValue?> LoadAsync(TKey key, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual async Task<Dictionary<TKey, TValue>> LoadManyAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<TKey, TValue>(keys.Count);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
            if (value != null)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
