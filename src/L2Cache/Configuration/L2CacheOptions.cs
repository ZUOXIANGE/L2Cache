namespace L2Cache.Configuration;

/// <summary>
/// L2Cache 全局配置选项。
/// <para>区域级配置（TTL、锁、空值策略等）请使用 <see cref="CacheRegionOptions"/>，通过
/// <c>AddL2Cache(...).AddCache&lt;TKey,TValue&gt;(name, configure)</c> 设置。</para>
/// </summary>
public class L2CacheOptions
{
    /// <summary>
    /// 是否启用本地缓存（L1）。
    /// </summary>
    public bool UseLocalCache { get; set; } = true;

    /// <summary>
    /// 是否启用 Redis 缓存（L2）。
    /// </summary>
    public bool UseRedis { get; set; }

    /// <summary>
    /// Redis 连接配置。
    /// </summary>
    public RedisCacheOptions Redis { get; set; } = new();

    /// <summary>
    /// 失效消息频道的名称前缀（频道完整名为 "{Prefix}:{CacheName}"）。
    /// </summary>
    public string InvalidationChannelPrefix { get; set; } = "l2cache:sync";

    /// <summary>
    /// 后台刷新的全局默认配置（区域可通过 <c>WithBackgroundRefresh</c> 覆盖）。
    /// </summary>
    public BackgroundRefreshOptions BackgroundRefresh { get; set; } = new();

    /// <summary>
    /// 遥测配置。
    /// </summary>
    public L2Cache.Abstractions.Telemetry.TelemetryOptions Telemetry { get; set; } = new();

    /// <summary>
    /// Redis 连接配置。
    /// </summary>
    public class RedisCacheOptions
    {
        /// <summary>连接字符串。</summary>
        public string ConnectionString { get; set; } = "localhost:6379";

        /// <summary>数据库索引。</summary>
        public int Database { get; set; }
    }
}

/// <summary>
/// 后台刷新配置。
/// </summary>
public class BackgroundRefreshOptions
{
    /// <summary>是否启用后台刷新。</summary>
    public bool Enabled { get; set; }

    /// <summary>默认刷新间隔（可被 ICacheRefreshPolicy 按Key 覆盖）。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
