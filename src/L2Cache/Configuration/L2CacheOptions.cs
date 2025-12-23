namespace L2Cache.Configuration;

/// <summary>
/// L2Cache 配置选项
/// </summary>
public class L2CacheOptions
{
    /// <summary>
    /// 是否启用本地缓存（L1）
    /// </summary>
    public bool UseLocalCache { get; set; } = true;

    /// <summary>
    /// 是否启用 Redis 缓存（L2）
    /// </summary>
    public bool UseRedis { get; set; } = false;

    /// <summary>
    /// Redis 缓存配置
    /// </summary>
    public RedisCacheOptions Redis { get; set; } = new RedisCacheOptions();

    /// <summary>
    /// 遥测配置
    /// </summary>
    public L2Cache.Abstractions.Telemetry.TelemetryOptions Telemetry { get; set; } = new L2Cache.Abstractions.Telemetry.TelemetryOptions();

    /// <summary>
    /// 后台刷新配置
    /// </summary>
    public BackgroundRefreshOptions BackgroundRefresh { get; set; } = new BackgroundRefreshOptions();

    /// <summary>
    /// 锁配置（用于解决缓存击穿和并发一致性问题）
    /// </summary>
    public LockOptions Lock { get; set; } = new LockOptions();

    /// <summary>
    /// Redis 缓存配置类
    /// </summary>
    public class RedisCacheOptions
    {
        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = "localhost:6379";

        /// <summary>
        /// 数据库索引
        /// </summary>
        public int Database { get; set; } = 0;
    }

    /// <summary>
    /// 后台刷新配置类
    /// </summary>
    public class BackgroundRefreshOptions
    {
        /// <summary>
        /// 是否启用后台刷新
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 刷新检查间隔
        /// </summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// 锁配置类
    /// </summary>
    public class LockOptions
    {
        /// <summary>
        /// 是否启用内存锁（防止单机缓存击穿）
        /// <para>默认开启。</para>
        /// </summary>
        public bool EnabledMemoryLock { get; set; } = true;

        /// <summary>
        /// 是否启用分布式锁（防止分布式环境缓存击穿）
        /// <para>需要开启 Redis。默认开启。</para>
        /// </summary>
        public bool EnabledDistributedLock { get; set; } = true;

        /// <summary>
        /// 锁等待超时时间
        /// </summary>
        public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 分布式锁过期时间（防止死锁）
        /// </summary>
        public TimeSpan DistributedLockExpiry { get; set; } = TimeSpan.FromSeconds(30);
    }
}
