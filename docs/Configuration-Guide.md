# 配置指南

L2Cache 的配置分为两层：**全局配置**（`L2CacheOptions`，整个应用一份）与**区域配置**（`CacheRegionOptions`，每个缓存区域一份）。

```csharp
builder.Services.AddL2Cache(options => { /* 全局配置 */ })
    .AddCache<TKey, TValue>("region-name", region => { /* 区域配置 */ });
```

## 全局配置（L2CacheOptions）

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `UseLocalCache` | `bool` | `true` | 是否启用 L1 内存缓存 |
| `UseRedis` | `bool` | `false` | 是否启用 L2 Redis 缓存 |
| `Redis.ConnectionString` | `string` | `"localhost:6379"` | Redis 连接字符串（StackExchange.Redis 格式，支持 `"host:port,password=...,ssl=true"` 等） |
| `Redis.Database` | `int` | `0` | Redis 数据库索引 |
| `InvalidationChannelPrefix` | `string` | `"l2cache:sync"` | 失效频道前缀，完整频道名为 `{Prefix}:{CacheName}` |
| `BackgroundRefresh` | `BackgroundRefreshOptions` | 见下 | 后台刷新全局默认值（可被区域覆盖） |
| `Telemetry` | `TelemetryOptions` | 见下 | 遥测配置 |

### BackgroundRefreshOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `false` | 是否启用后台刷新（区域级需配合 `WithBackgroundRefresh()`） |
| `Interval` | `TimeSpan` | `1 分钟` | 默认刷新间隔，可被 `ICacheRefreshPolicy<TKey, TValue>` 按 Key 覆盖 |

### TelemetryOptions（命名空间 `L2Cache.Abstractions.Telemetry`）

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableTelemetry` | `bool` | `true` | 遥测总开关（关闭时完全零开销） |
| `EnableTracing` | `bool` | `true` | 是否输出 Activity（链路追踪） |
| `EnableMetrics` | `bool` | `true` | 是否记录 Meter 指标 |
| `ActivitySourceName` | `string` | `"L2Cache"` | ActivitySource / OTel 订阅名 |
| `MetricsPrefix` | `string` | `"l2cache"` | 指标名称前缀 |
| `RecordCacheKeys` | `bool` | `false` | 遥测中是否记录缓存键（注意敏感数据）。默认 `false`：Trace 与 Metrics 均**不**落真实键 |
| `RecordCacheValueSize` | `bool` | `true` | 是否记录缓存值大小 |

> **生效条件**：`EnableTelemetry=true` 时默认仍是 NoOp 提供程序，需额外调用
> `AddL2CacheTelemetry()` 才会替换为 `DefaultTelemetryProvider`；随后在 OpenTelemetry 中订阅
> `AddMeter("L2Cache")` 与 `AddSource("L2Cache")`（名称见 `ActivitySourceName`）。

### 指标语义（Metrics，前缀默认 `l2cache_`）

| 指标 | 类型 | 说明 | 主要维度 |
|------|------|------|---------|
| `l2cache_cache_requests_total` | Counter | 请求计数（见下方“计数语义”） | `operation`、`cache_name`、`cache_type`、`result`、`source` |
| `l2cache_cache_hits_total` / `misses_total` | Counter | 按层级的命中/未命中 | `cache_type=L1|L2`、`operation=get` |
| `l2cache_cache_errors_total` | Counter | 异常缓存操作 | `operation`、`result=error` |
| `l2cache_cache_response_time_seconds` | Histogram | 耗时（按层级/操作/回源分别记录） | 同 requests |
| `l2cache_cache_size_bytes` | Histogram | 写入值大小 | `operation=set` |
| `l2cache_cache_item_count` | Observable 仪表 | 进程内 L1 条目总数（快照） | 无 |
| `l2cache_cache_connections` | Observable 仪表 | Redis 连接状态（1=已连接/0=断开） | 无 |

**计数语义（重要）**：
- 单键读（get / get_or_load）会依次探测 L1、L2，**每层探测各计 1 次** `requests_total`
  （所以 `requests_total` 是“层级探测数”而非“API 调用数”；命中率请按 `cache_type` 分层计算）。
- `operation=load` 且 `source=datasource` 表示**回源（数据库加载）**；回源成功/失败分别记
  `result=success/error`。旧版回源不产指标的盲区已修复。
- 批量操作按“批”计数：`operation=batch_get|batch_get_or_load|batch_put|batch_evict`，
  额外携带 `key_count` 维度。

### 链路语义（Tracing，span 名如 `cache.get_or_load`）

单键读相关 span 上会标注本次请求数据的**来源层级**：

| 属性 | 取值 | 含义 |
|------|------|------|
| `cache.level` | `L1` / `L2` | 本次返回的数据命中自 L1（进程内存）还是 L2（Redis） |
| `cache.source` | `datasource` | 本次请求触发了回源（从数据库/数据源加载，见 `loader.LoadAsync`） |
| `key_pattern` | 仅配置 `RecordCacheKeys=true` 时出现 | 真实缓存键（超过 `MaxKeyLength` 会截断）。默认关闭，避免向 Trace 泄露敏感键 |

批量 span（`cache.batch_*`）不标来源层级，其命中/耗时请查 Metrics。

## 区域配置（CacheRegionOptions）

每个区域独立配置，互不影响：

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `CacheName` | `string` | 由 `AddCache`指定 | 区域名称（注册后不可修改） |
| `DefaultTtl` | `TimeSpan?` | `null`（不过期） | 调用方未显式传 `expiry` 时 L2 使用的 TTL；`null` 表示永不过期 |
| `MaxL1Ttl` | `TimeSpan` | `5 分钟` | L1 TTL 上限。即使 L2 TTL 很长，L1 也不会缓存超过该时长——这是 Pub/Sub 丢消息时的最终一致性兜底 |
| `Lock` | `LockOptions` | 见下 | 防击穿与并发写控制 |
| `NullValue` | `NullValueOptions` | 见下 | 空值缓存（防穿透） |
| `PublishInvalidation` | `bool` | `true` | L2 写入/删除后是否发布失效消息。单机部署或只读场景可关闭以减少开销 |
| `BackgroundRefresh` | `BackgroundRefreshOptions` | 继承全局 | 仅在 `WithBackgroundRefresh()` 启用时生效 |

### LockOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnabledMemoryLock` | `bool` | `true` | 进程内内存锁（分段锁），合并单机内并发回源 |
| `EnabledDistributedLock` | `bool` | `true` | Redis 分布式锁，合并跨节点并发回源（需 `UseRedis`） |
| `LockTimeout` | `TimeSpan` | `10 秒` | 锁等待上限，超时后降级为无锁直读/直写（可用性优先） |
| `DistributedLockExpiry` | `TimeSpan` | `30 秒` | 分布式锁自动过期时间（防死锁） |

### NullValueOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `false` | 回源得到 `null` 时是否写入空值哨兵（`@@NULL@@`），防止穿透 |
| `Ttl` | `TimeSpan` | `30 秒` | 空值缓存项的 TTL，建议较短以减小不一致窗口 |

## 多区域配置示例

不同业务通常有不同的过期与防护策略：

```csharp
builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;
    options.UseRedis = true;
    options.Redis.ConnectionString = redis;
})
// 商品详情：半小时 TTL，开启空值缓存
.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(30);
    region.NullValue.Enabled = true;
})
.WithLoader<ProductLoader>()

// 会话数据：较短 TTL，不广播失效（单节点写入）
.AddCache<string, SessionDto>("sessions", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(10);
    region.PublishInvalidation = false;
})

// 配置数据：长缓存 + 后台刷新
.AddCache<string, AppSettingsDto>("appsettings", region =>
{
    region.DefaultTtl = TimeSpan.FromHours(6);
})
.WithLoader<SettingsLoader>()
.WithBackgroundRefresh(refresh => refresh.Interval = TimeSpan.FromMinutes(2));
```

## TTL 的实际计算规则

- 显式传入的 `expiry`（如 `GetOrLoadAsync(id, TimeSpan.FromMinutes(1))`）优先级最高。
- 未传入时使用区域的 `DefaultTtl`。
- 空值缓存项固定使用 `NullValue.Ttl`。
- L1 实际 TTL = `min(该 L2 TTL, MaxL1Ttl)`，保证失效兜底。
