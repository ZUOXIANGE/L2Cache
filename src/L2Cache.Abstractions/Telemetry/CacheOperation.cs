namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 缓存操作类型枚举。
/// <para>
/// 定义了缓存系统中所有可能的标准操作类型，用于遥测记录、日志分析和性能监控。
/// </para>
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// 无操作。
    /// </summary>
    None,

    /// <summary>
    /// 获取单个缓存项。
    /// </summary>
    Get,

    /// <summary>
    /// 设置/更新单个缓存项。
    /// </summary>
    Set,

    /// <summary>
    /// 删除单个缓存项。
    /// </summary>
    Delete,

    /// <summary>
    /// 移除单个缓存项（通常指从 L1 中移除）。
    /// </summary>
    Remove,

    /// <summary>
    /// 清空整个缓存区域。
    /// </summary>
    Clear,

    /// <summary>
    /// 检查缓存项是否存在。
    /// </summary>
    Exists,

    /// <summary>
    /// 批量获取多个缓存项。
    /// </summary>
    BatchGet,

    /// <summary>
    /// 批量设置多个缓存项。
    /// </summary>
    BatchSet,

    /// <summary>
    /// 批量删除多个缓存项。
    /// </summary>
    BatchDelete,

    /// <summary>
    /// 重新加载缓存项（强制回源）。
    /// </summary>
    Reload,

    /// <summary>
    /// 驱逐缓存项（通常指因内存压力或策略而被动移除）。
    /// </summary>
    Evict
}
