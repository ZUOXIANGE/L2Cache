namespace L2Cache.Abstractions.Stores;

/// <summary>
/// L2（分布式存储）的读取结果。
/// <para>
/// <see cref="Found"/> 为 false 表示未命中；为 true 时 <see cref="Payload"/> 为存储的原始字节。
/// 空值哨兵的识别由编排层通过 <see cref="Abstractions.Policies.INullValuePolicy"/> 完成。
/// </para>
/// </summary>
public readonly record struct StoreEntry(bool Found, ReadOnlyMemory<byte> Payload)
{
    /// <summary>未命中的空结果。</summary>
    public static readonly StoreEntry NotFound = new(false, default);
}

/// <summary>
/// L1（本地内存存储）的读取结果。
/// <para>
/// L1 是"对象缓存"：存储的是已反序列化的实例，因此命中后无需再次反序列化。
/// <see cref="IsNullValue"/> 用于区分"命中的空值"（防止缓存穿透）与普通值。
/// </para>
/// </summary>
public readonly record struct L1Entry(bool Found, bool IsNullValue, object? Value)
{
    /// <summary>未命中的空结果。</summary>
    public static readonly L1Entry NotFound;
}

/// <summary>
/// L1 本地内存存储适配接口。
/// <para>
/// 实现应线程安全；Key 为包含区域前缀的完整 Key（"{CacheName}:{Key}"）。
/// </para>
/// </summary>
public interface IL1CacheStore
{
    /// <summary>读取一个缓存项。</summary>
    /// <param name="key">完整缓存 Key。</param>
    /// <returns>读取结果。</returns>
    L1Entry GetValue(string key);

    /// <summary>写入一个缓存项。<paramref name="value"/> 为 null 时内部存储为空值标记。</summary>
    /// <param name="key">完整缓存 Key。</param>
    /// <param name="value">已反序列化的对象实例。</param>
    /// <param name="ttl">过期时间。</param>
    void SetValue(string key, object? value, TimeSpan? ttl);

    /// <summary>移除一个缓存项。</summary>
    void Remove(string key);

    /// <summary>检查缓存项是否存在（含空值标记）。</summary>
    bool Exists(string key);
}

/// <summary>
/// L2 分布式存储适配接口（默认实现为 Redis）。
/// <para>
/// 操作对象为原始字节，序列化由编排层完成。实现应容忍连接故障：
/// 读取失败返回未命中、写入失败返回 false，并向遥测/日志上报错误，而不是抛出异常。
/// </para>
/// </summary>
public interface IL2CacheStore
{
    /// <summary>读取一个缓存项。</summary>
    Task<StoreEntry> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>写入一个缓存项。</summary>
    /// <param name="key">完整缓存 Key。</param>
    /// <param name="payload">序列化后的字节载荷。</param>
    /// <param name="ttl">过期时间；null 表示使用实现默认值。</param>
    /// <param name="onlyIfAbsent">true 时为 NX 模式（仅当 Key 不存在时写入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否写入成功。</returns>
    Task<bool> SetAsync(string key, ReadOnlyMemory<byte> payload, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default);

    /// <summary>移除一个缓存项。</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>检查缓存项是否存在。</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>批量读取（MGET）。</summary>
    Task<Dictionary<string, StoreEntry>> GetManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    /// <summary>批量写入（Pipeline）。</summary>
    /// <param name="items">Key 与载荷的映射。</param>
    /// <param name="ttl">过期时间。</param>
    /// <param name="onlyIfAbsent">true 时为 NX 模式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际写入成功的 Key 集合。</returns>
    Task<HashSet<string>> SetManyAsync(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> items, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default);

    /// <summary>批量移除（DEL）。</summary>
    /// <returns>实际移除的数量。</returns>
    Task<long> RemoveManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    /// <summary>获取分布式锁（用于防击穿）。实现可返回 false 表示不可用。</summary>
    Task<bool> AcquireLockAsync(string lockKey, string token, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>释放分布式锁。仅当 token 匹配时释放。</summary>
    Task<bool> ReleaseLockAsync(string lockKey, string token, CancellationToken cancellationToken = default);
}
