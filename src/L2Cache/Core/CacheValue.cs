namespace L2Cache.Core;

/// <summary>缓存读取状态。</summary>
public enum CacheStatus
{
    /// <summary>命中有效值。</summary>
    Found,

    /// <summary>命中空值（该 Key 在数据源中不存在，已被空值缓存标记）。</summary>
    FoundNull,

    /// <summary>未命中。</summary>
    NotFound
}

/// <summary>缓存读取结果：显式区分"有效值 / 空值 / 未命中"，消除 default(TValue) 的歧义。</summary>
public readonly record struct CacheValue<TValue>(CacheStatus Status, TValue? Value)
{
    public bool IsFound => Status == CacheStatus.Found;

    public bool IsFoundNull => Status == CacheStatus.FoundNull;

    public bool IsNotFound => Status == CacheStatus.NotFound;
}

/// <summary>
/// <see cref="CacheValue{TValue}"/> 的工厂（CA1000：避免在泛型类型上声明静态成员）。
/// </summary>
public static class CacheValue
{
    /// <summary>创建"命中有效值"结果。</summary>
    public static CacheValue<TValue> Found<TValue>(TValue value) => new(CacheStatus.Found, value);

    /// <summary>创建"命中空值"结果。</summary>
    public static CacheValue<TValue> FoundNull<TValue>() => new(CacheStatus.FoundNull, default);

    /// <summary>创建"未命中"结果。</summary>
    public static CacheValue<TValue> NotFound<TValue>() => new(CacheStatus.NotFound, default);
}
