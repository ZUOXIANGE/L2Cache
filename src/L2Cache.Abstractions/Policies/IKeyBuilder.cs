namespace L2Cache.Abstractions.Policies;

/// <summary>
/// Key 构建策略。将业务 Key 转换为字符串形式（不含区域前缀）。
/// </summary>
/// <typeparam name="TKey">业务 Key 类型。</typeparam>
public interface IKeyBuilder<TKey> where TKey : notnull
{
    /// <summary>构建业务 Key 的字符串表示。</summary>
    string Build(TKey key);
}

/// <summary>
/// 默认 Key 构建策略。
/// <para>简单类型（string / 枚举 / 基元值类型）直接 ToString()；复杂类型抛出异常，要求业务提供自定义实现。</para>
/// </summary>
public sealed class DefaultKeyBuilder<TKey> : IKeyBuilder<TKey> where TKey : notnull
{
    public string Build(TKey key)
    {
        if (key is string || key is ValueType)
        {
            return key.ToString() ?? string.Empty;
        }

        throw new InvalidOperationException(
            $"缓存 Key 类型 {typeof(TKey).Name} 为复杂类型，请通过 IKeyBuilder<{typeof(TKey).Name}> 提供自定义 Key 构建逻辑。");
    }
}
