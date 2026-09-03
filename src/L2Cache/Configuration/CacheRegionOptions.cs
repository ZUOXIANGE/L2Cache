namespace L2Cache.Configuration;

/// <summary>
/// 缓存区域配置。
/// <para>每个缓存区域（由 <c>AddCache&lt;TKey,TValue&gt;(name, configure)</c> 声明）独立拥有一份配置，
/// 区域名同时作为 Redis Key 前缀（"{CacheName}:{Key}"）与失效频道后缀。</para>
/// </summary>
public class CacheRegionOptions
{
    /// <summary>区域名称（由 AddCache 指定，不可修改）。</summary>
    public string CacheName { get; internal set; } = "";

    /// <summary>
    /// 默认 L2 TTL。调用方未显式指定过期时间时使用；null 表示不过期。
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>
    /// L1 TTL 上限。L1 缓存时间不会超过该值，作为 Pub/Sub 丢消息时的最终一致性兜底。
    /// </summary>
    public TimeSpan MaxL1Ttl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>锁配置（防击穿与并发写冲突）。</summary>
    public LockOptions Lock { get; set; } = new();

    /// <summary>空值缓存配置（防穿透）。</summary>
    public NullValueOptions NullValue { get; set; } = new();

    /// <summary>
    /// 是否在 L2 写入/删除后发布失效消息（通知其他节点清除 L1）。
    /// 单机部署或只读场景可关闭以减少开销。
    /// </summary>
    public bool PublishInvalidation { get; set; } = true;

    /// <summary>后台刷新配置（仅在 WithBackgroundRefresh 启用时生效）。</summary>
    public BackgroundRefreshOptions BackgroundRefresh { get; internal set; } = new();
}

/// <summary>
/// 锁配置。
/// </summary>
public class LockOptions
{
    /// <summary>
    /// 是否启用进程内内存锁（防止单机缓存击穿）。默认开启。
    /// </summary>
    public bool EnabledMemoryLock { get; set; } = true;

    /// <summary>
    /// 是否启用分布式锁（防止跨节点缓存击穿）。需要启用 Redis。默认开启。
    /// </summary>
    public bool EnabledDistributedLock { get; set; } = true;

    /// <summary>
    /// 锁等待超时时间。超时后降级为无锁直读/直写（可用性优先）。
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 分布式锁的自动过期时间（防死锁）。
    /// </summary>
    public TimeSpan DistributedLockExpiry { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 空值缓存配置。
/// </summary>
public class NullValueOptions
{
    /// <summary>
    /// 是否缓存回源得到的空值（防止缓存穿透）。默认关闭。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 空值缓存项的 TTL。建议设置较短时间（如 30 秒）以减小数据不一致窗口。
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);
}
