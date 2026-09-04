# 遥测与可观测性（OpenTelemetry）

L2Cache 内置 OpenTelemetry 支持，可同时产出 **Metrics（指标）** 与 **Tracing（分布式链路）**。本文说明如何接入、有哪些信号、以及各信号的语义。

---

## 1. 接入与开关

### 1.1 安装并启用

遥测实现位于独立的 `L2Cache.Telemetry` 包：

```bash
dotnet add package L2Cache.Telemetry
```

```csharp
using L2Cache;
using L2Cache.Extensions;

var l2Cache = builder.Services.AddL2Cache(options =>
{
    // ... 缓存全局/区域配置
    options.Telemetry.ActivitySourceName = "L2Cache"; // 订阅名（可选，默认即 "L2Cache"）
    options.Telemetry.MetricsPrefix = "l2cache";      // 指标名前缀（可选，默认 "l2cache"）
});

// 将默认的 NoOp 提供程序替换为 DefaultTelemetryProvider
builder.Services.AddL2CacheTelemetry();

// 订阅 OpenTelemetry（名称与 ActivitySourceName 一致）
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("L2Cache"))
    .WithTracing(t => t.AddSource("L2Cache"));
```

完整可运行示例见 [examples/L2Cache.Examples/Program.cs](../examples/L2Cache.Examples/Program.cs)
（含 OTLP Exporter 配置，通过环境变量 `OTEL_EXPORTER_OTLP_ENDPOINT` 指定端点）。

### 1.2 开关与零开销

| 开关 | 说明 |
|------|------|
| 默认（未调用 `AddL2CacheTelemetry()`） | 使用 `NoOpTelemetryProvider`，**所有遥测调用均为空操作、零分配**。 |
| `Telemetry.EnableTelemetry = false` | 关闭指标/追踪输出。 |
| `Telemetry.EnableTracing = false` | 关闭 span；开启时采样交由 OpenTelemetry Sampler 控制。 |
| `Telemetry.EnableMetrics = false` | 关闭指标输出。 |

> 采样建议：不要在库内做随机采样，直接配置 OpenTelemetry 的 `Sampler`（如
> `ParentBased(AlwaysOn / TraceIdRatioBased)`），保证父子链路采样一致。

### 1.3 有效开关集（TelemetryOptions）

精简后的可配置属性（其余历史选项已移除）：

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `EnableTelemetry` | `true` | 遥测总开关 |
| `ActivitySourceName` | `"L2Cache"` | ActivitySource / Meter 名称（OTel 订阅名） |
| `EnableMetrics` | `true` | 是否记录 Meter 指标 |
| `MetricsPrefix` | `"l2cache"` | 指标名称前缀 |
| `EnableTracing` | `true` | 是否输出 span |
| `RecordCacheKeys` | `false` | 是否记录真实缓存键（注意敏感数据） |
| `RecordCacheValueSize` | `true` | 是否记录写入值大小 |
| `MaxKeyLength` | `100` | 记录键时的截断长度 |

---

## 2. Metrics 清单与语义

指标名格式为 `{MetricsPrefix}_cache_xxx`（下文以默认前缀 `l2cache` 展示）。

### 2.1 指标清单

| 指标 | 类型 | 说明 |
|------|------|------|
| `l2cache_cache_requests_total` | Counter | 请求/探测计数（含回源 load 与批量 batch_*） |
| `l2cache_cache_hits_total` | Counter | 命中数（按 `cache_type` 分层） |
| `l2cache_cache_misses_total` | Counter | 未命中数（按 `cache_type` 分层） |
| `l2cache_cache_errors_total` | Counter | 异常缓存操作数 |
| `l2cache_cache_response_time_seconds` | Histogram | 耗时分布 |
| `l2cache_cache_size_bytes` | Histogram | 写入值大小分布 |
| `l2cache_cache_item_count` | Observable 仪表 | L1 条目总数（进程级绝对快照） |
| `l2cache_cache_connections` | Observable 仪表 | Redis 连接状态（1=已连接 / 0=断开） |

### 2.2 常用维度（tag）

| 维度 | 取值示例 | 说明 |
|------|---------|------|
| `cache_name` | `"products"` | 缓存区域名 |
| `operation` | `get` / `set` / `evict` / `exists` / `load` / `batch_get` / `batch_get_or_load` / `batch_put` / `batch_evict` | 操作类型 |
| `cache_type` | `L1` / `L2` | 命中的缓存层级 |
| `result` | `hit` / `miss` / `success` / `error` | 结果 |
| `source` | `datasource` | 回源（数据库/数据源）标记 |
| `key_pattern` | 仅 `RecordCacheKeys=true` 时出现 | 记录的真实键（超 `MaxKeyLength` 截断） |
| `key_count` | `100` | 批量操作的批大小 |

### 2.3 计数语义（重要）

- **单键读是“层级探测”计数**：一次 `get`/`get_or_load` 会依次探测 L1、L2，
  `requests_total` 对每层探测各 +1。因此算命中率时请按 `cache_type` 分层计算，
  不要用 `hits_total / 调用次数`。
- **回源可见**：loader 触发回源时以 `operation=load` 记录请求与耗时，并带
  `source=datasource`、`result=success|error`（旧版回源完全不产指标的盲区已修复）。
- **批量按“批”计数**：`batch_get_or_load` 等按一次调用 +1，附带 `key_count` 维度，
  不做逐 Key 命中统计（逐 Key 命中请使用单键读指标）。
- **状态仪表是绝对快照**：`cache_item_count` / `cache_connections` 由 OTel 采集端定时回调，
  反映当前值而非增量。
- 单键写入：`operation=set` 会记录 `cache_type=L2`（写入以 L2 为准），并在
  `RecordCacheValueSize=true`（默认）时记录 `cache_size_bytes`。

---

## 3. Tracing 属性与键策略

### 3.1 Span 清单

默认启用后，每次公开操作产生一个 span：

| Span 名 | 说明 |
|---------|------|
| `cache.get` | 查询缓存（不回源） |
| `cache.get_or_load` | Cache-Aside：读 → 锁 → 回源 → 回填 |
| `cache.set` / `cache.put_if_absent` | 写入 |
| `cache.evict` | 删除单键 |
| `cache.reload` | 强制回源刷新 |
| `cache.exists` | 判断存在 |
| `cache.batch_get` / `cache.batch_get_or_load` / `cache.batch_evict` | 批量操作 |

### 3.2 单键读的来源标注

在单键读相关的 span（`cache.get`、`cache.get_or_load`、`cache.reload` 等）上，可通过属性判断
**本次返回的数据到底来自哪一层**：

| 属性 | 取值 | 含义 |
|------|------|------|
| `cache.level` | `L1` / `L2` | 本次命中自 L1（进程内存）还是 L2（Redis）。仅命中时打标。 |
| `cache.source` | `datasource` | 本次请求**触发过回源**（数据库/数据源加载）。 |
| `cache_name` | 区域名 | 恒有 |
| `key_pattern` | 见 3.3 | 键记录策略 |

> 例如：一次 `get_or_load` 若最终从数据库返回，则该 span 只带 `cache.source=datasource`
> 而无 `cache.level`；若命中 L1，则只带 `cache.level=L1`。

批量 span（`cache.batch_*`）不做逐 Key 来源标注，只带 `key_count` 与 `cache_name`；
批量命中/耗时请从 Metrics 观察。

### 3.3 键的隐私策略

- `RecordCacheKeys` 默认 **false**：span 与指标都**不会**出现真实缓存键
  （`key_pattern` 维度省略），避免把敏感 Key 落进 Jaeger / OTLP 后端。
- 置为 `true` 后，`key_pattern` 记录“由 `KeyBuilder` 构建的缓存键”，
  长度超过 `MaxKeyLength`（默认 100）会被截断为 `前缀...`。
- 建议：生产环境关闭，仅在排查单条请求时按需开启。

### 3.4 异常

- 缓存操作抛错：会在当前 span 上追加 `exception` 事件并置
  `ActivityStatusCode.Error`，同时累计 `l2cache_cache_errors_total`。
- L2 故障采用降级语义（视为未命中/写入失败），不会抛错，属正常路径而非异常。

---

## 4. 常见问题

- **为何加了 `AddOpenTelemetry` 却看不到数据？**
  检查：① 是否调用过 `AddL2CacheTelemetry()`；② `AddMeter/AddSource` 名称是否与
  `Telemetry.ActivitySourceName` 一致（默认 `L2Cache`）；③ OTLP 端点/采样配置是否正确。
- **指标里没有回源（数据库）数据？**
  确认回源路径（`WithLoader` + `GetOrLoadAsync`/`ReloadAsync`）实际发生过，
  且查询维度为 `operation=load` + `source=datasource`。
- **Span 里看不到键？**
  这是默认行为（`RecordCacheKeys=false`）。需要排查单键时可临时开启并注意脱敏。
- **为什么 `requests_total` 比调用次数多？**
  因为它统计的是“层级探测次数”（L1、L2 各一次），详见 2.3。
