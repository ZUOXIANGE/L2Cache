namespace L2Cache.Abstractions.Telemetry;

/// <summary>
/// 缓存级别枚举
/// 用于区分不同层级的缓存操作
/// </summary>
public enum CacheLevel
{
    /// <summary>
    /// 一级缓存（本地缓存）
    /// </summary>
    L1 = 1,

    /// <summary>
    /// 二级缓存（Redis缓存）
    /// </summary>
    L2 = 2,

    /// <summary>
    /// 所有缓存级别
    /// </summary>
    Both = 3
}