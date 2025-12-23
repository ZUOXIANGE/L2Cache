# L2Cache 配置指南

本指南详细介绍了 L2Cache 的所有配置选项。所有配置都通过 `L2CacheOptions` 类进行管理。

## 基础配置

在 `AddL2Cache` 方法中配置 `L2CacheOptions`：

```csharp
services.AddL2Cache(options =>
{
    // ... 配置项
});
```

### 1. 本地缓存 (L1)

配置基于 `IMemoryCache` 的进程内缓存。

```csharp
// 启用/禁用本地缓存 (默认: true)
options.UseLocalCache = true;
```

### 2. Redis 缓存 (L2)

配置基于 Redis 的分布式缓存。

```csharp
// 启用/禁用 Redis 缓存 (默认: false)
options.UseRedis = true;

// Redis 连接字符串 (StackExchange.Redis 格式)
options.Redis.ConnectionString = "localhost:6379,password=...";

// Redis 数据库索引 (默认: 0)
options.Redis.Database = 0;
```

### 3. 序列化配置

L2Cache 默认使用 `System.Text.Json`。

#### 使用 MemoryPack (高性能二进制序列化)

如需使用 MemoryPack，请手动注册服务：

```csharp
// 在 AddL2Cache 之后注册
services.AddSingleton<ICacheSerializer, MemoryPackCacheSerializer>();
```

### 4. 遥测与监控 (Telemetry)

配置 OpenTelemetry 支持（Metrics, Tracing, Logging）以及健康检查。

> **注意**: 此功能需要安装 `L2Cache.Telemetry` NuGet 包，并调用 `.AddL2CacheTelemetry()` 扩展方法。

```csharp
// 启用所有遥测功能 (默认: true)
options.Telemetry.EnableTelemetry = true;

// --- 指标 (Metrics) ---
// 启用指标收集 (默认: true)
options.Telemetry.EnableMetrics = true;
// 指标前缀 (默认: "l2cache")
options.Telemetry.MetricsPrefix = "l2cache";
// 指标收集间隔 (默认: 30秒)
options.Telemetry.MetricsCollectionInterval = TimeSpan.FromSeconds(30);

// --- 追踪 (Tracing) ---
// 启用分布式追踪 (默认: true)
options.Telemetry.EnableTracing = true;
// 活动源名称 (默认: "L2Cache")
options.Telemetry.ActivitySourceName = "L2Cache";

// --- 日志 (Logging) ---
// 启用日志记录 (默认: true)
options.Telemetry.EnableLogging = true;

// --- 健康检查 (Health Check) ---
// 启用健康检查 (默认: true)
// 会自动注册 IHealthCheck，可用于 /health 端点
options.Telemetry.EnableHealthCheck = true;
// 健康检查间隔 (默认: 60秒)
options.Telemetry.HealthCheckInterval = TimeSpan.FromSeconds(60);
```

### 5. 后台刷新 (Background Refresh)

配置缓存自动刷新策略，防止缓存雪崩。

```csharp
// 启用/禁用后台刷新 (默认: false)
options.BackgroundRefresh.Enabled = true;

// 默认刷新检查间隔 (默认: 1分钟)
options.BackgroundRefresh.Interval = TimeSpan.FromMinutes(1);
```

### 6. 并发锁配置 (Lock Options)

配置内存锁和分布式锁策略，用于解决缓存击穿（Cache Stampede）和并发写入一致性问题。

```csharp
// 启用内存锁 (默认: false)
// 使用 SemaphoreSlim 进行细粒度的进程内锁定
options.Lock.EnabledMemoryLock = true;

// 启用分布式锁 (默认: false)
// 使用 Redis 分布式锁，确保多实例间的并发控制
options.Lock.EnabledDistributedLock = true;

// 锁等待超时时间 (默认: 5秒)
// 获取锁的最大等待时间，超过此时间将放弃获取锁（通常会降级为无锁执行或抛出异常）
options.Lock.LockTimeout = TimeSpan.FromSeconds(5);

// 分布式锁过期时间 (默认: 10秒)
// 防止死锁，锁在 Redis 中的自动过期时间
options.Lock.DistributedLockExpiry = TimeSpan.FromSeconds(10);
```
