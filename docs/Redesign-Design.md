# L2Cache 重架构详细设计文档

> 状态：评审中（Draft v1）
> 目标读者：L2Cache 维护者与贡献者
> 关联问题：继承驱动的上帝类设计、服务定位器反模式、泛型静态状态、Pub/Sub 失效链路脆弱、全局配置无法按区域定制

---

## 1. 背景与设计目标

### 1.1 现状问题清单

| # | 问题 | 位置 | 严重度 |
|---|------|------|--------|
| P1 | `AbstractCacheService` 上帝类：L1/L2 读写、锁、空值缓存、Key 构建、序列化、遥测全部耦合在一个约 1200 行的泛型基类 | `src/L2Cache/AbstractCacheService.cs` | 高 |
| P2 | 服务定位器反模式：构造函数通过 `serviceProvider.GetService(...)` 隐式取依赖，配置错误运行时才暴露（Warning） | `src/L2Cache/L2CacheService.cs` | 高 |
| P3 | 泛型静态状态：`static IsSubscribed` 每个封闭泛型类型一份；Pub/Sub 订阅发生在 Scoped 服务构造函数中 | `src/L2Cache/L2CacheService.cs` | 高 |
| P4 | `GetCacheName()` 覆写不一致：`L2CacheService` 用 `typeof(TValue).Name` 构建缓存名与 Pub/Sub 频道，用户覆写 `GetCacheName()` 后失效同步错位 | `src/L2Cache/L2CacheService.cs` | 高 |
| P5 | 全局单份 `L2CacheOptions`：TTL、锁策略、空值策略无法按缓存区域定制 | `src/L2Cache/Configuration/L2CacheOptions.cs` | 中 |
| P6 | 注册体验差：开放泛型注册迫使用户子类编写携带 `IServiceProvider` 的构造函数 | `src/L2Cache/ServiceCollectionExtensions.cs` | 中 |
| P7 | Pub/Sub 失效无兜底：消息仅含 Key 字符串、无版本号；丢消息后 L1 脏数据依赖硬编码 5 分钟 TTL | `AbstractCacheService.GetLocalCacheExpiry` | 中 |
| P8 | 分布式锁自旋等待实现（20ms 起步指数退避，上限 200ms）内嵌在业务方法中，无法替换 | `AbstractCacheService.GetOrLoadAsync` | 低 |
| P9 | 批量操作中 `BatchGetOrLoadAsync` 对已缓存空值（FoundNull）的 Key 会重复回源（`result` 不含空值 Key） | `AbstractCacheService.BatchGetOrLoadAsync` | 中 |

### 1.2 设计目标

1. **组合优于继承**：核心管道由单一单例编排器承载，行为差异全部下沉为可插拔策略。
2. **依赖显式化**：所有依赖构造函数注入，配置错误启动即失败（`ValidateOnStart`）。
3. **区域化配置**：每个缓存区域（Cache Region）独立配置 TTL、锁、空值、失效策略。
4. **失效链路加固**：订阅器单例化、频道名与区域名一致、消息带版本号、L1 TTL 上限可配置。
5. **平滑迁移**：旧公开 API（`AbstractCacheService` / `L2CacheService` / `ICacheService`）保持兼容，内部改为转发新管道。

### 1.3 非目标

- 不引入 .NET `HybridCache` 替代自研编排（缺少 Pub/Sub 同步、区域化策略、Cache-Aside 基类，均为本项目差异化价值）。
- 不做多 L2 后端（Garnet/Valkey 兼容 RESP，`IL2CacheStore` 一个接口预留即可，暂不实现多后端）。
- 不改变 Redis 数据格式（兼容旧数据：`@@NULL@@` 哨兵字符串保留）。

---

## 2. 目标架构总览

```mermaid
graph TD
    subgraph 业务层
        A[业务代码] --> B[ICacheClient&lt;TKey,TValue&gt;]
        A2[业务代码-旧] --> B2[L2CacheService&lt;TKey,TValue&gt; 兼容层]
    end

    subgraph 编排层 Core（单例）
        B --> C[CacheOrchestrator]
        B2 --> C
        C --> C1[读管道: L1→L2→回源]
        C --> C2[写管道: 加锁→L2→L1→失效广播]
        C --> C3[批量管道: MGET/Pipeline/回源合并]
    end

    subgraph 策略层（每区域一份）
        C --> D1[IKeyBuilder&lt;TKey&gt;]
        C --> D2[IExpiryPolicy]
        C --> D3[ILockPolicy]
        C --> D4[INullValuePolicy]
        C --> D5[ILoader&lt;TKey,TValue&gt;]
    end

    subgraph 存储层
        C --> E1[IL1CacheStore<br/>MemoryCacheStore]
        C --> E2[IL2CacheStore<br/>RedisCacheStore]
    end

    subgraph 失效层
        C2 --> F[ICacheInvalidationBus]
        F --> F1[RedisPubSubInvalidationBus]
        F1 --> F2[InvalidationSubscriber<br/>HostedService 单例]
    end

    subgraph 可观测层
        C --> G[ITelemetryProvider]
        C --> H[ILogger]
    end
```

**核心思想**：`CacheOrchestrator` 是唯一的复杂逻辑持有者（单例、非泛型）；泛型只出现在两个薄的边界上 —— 顶层的 `CacheClient<TKey,TValue>` 门面和底层的 `ILoader<TKey,TValue>` 回源委托。

---

## 3. 模块详细设计

### 3.1 存储层（新项目：`L2Cache.Stores` 或并入 Core）

存储操作对象为 `ReadOnlyMemory<byte>`，**序列化不在存储层发生**。

```csharp
namespace L2Cache.Stores;

/// <summary>存储条目。区分"未命中"与"命中的空值"。</summary>
public readonly record struct StoreEntry(bool Found, ReadOnlyMemory<byte> Payload);

/// <summary>L1 本地存储适配。线程安全。</summary>
public interface IL1CacheStore
{
    StoreEntry Get(string key);
    void Set(string key, ReadOnlyMemory<byte> payload, TimeSpan? ttl);
    void Remove(string key);
    bool Exists(string key);
    /// <summary>批量查询，返回命中项；缺失的 Key 不出现在结果中。</summary>
    Dictionary<string, StoreEntry> GetMany(IEnumerable<string> keys);
}

/// <summary>L2 分布式存储适配。实现应容忍连接故障（内部降级，不抛出）。</summary>
public interface IL2CacheStore
{
    Task<StoreEntry> GetAsync(string key, CancellationToken ct = default);
    Task<bool> SetAsync(string key, ReadOnlyMemory<byte> payload, TimeSpan? ttl, bool onlyIfAbsent, CancellationToken ct = default);
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, StoreEntry>> GetManyAsync(IReadOnlyList<string> keys, CancellationToken ct = default);
    /// <summary>Pipeline 批量写入。返回实际写入成功的 Key（onlyIfAbsent 时有意义）。</summary>
    Task<HashSet<string>> SetManyAsync(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> items, TimeSpan? ttl, bool onlyIfAbsent, CancellationToken ct = default);
    Task<long> RemoveManyAsync(IReadOnlyList<string> keys, CancellationToken ct = default);
}
```

**实现**：

- `MemoryCacheStore`：包装 `IMemoryCache`，内部持有 `ConcurrentDictionary<string, byte>` 跟踪已写入的 Key（替代现在散落的 `CacheKeyTracker` 职责，用于批量/清空场景）。
- `RedisCacheStore`：包装 `IConnectionMultiplexer`。空值哨兵 `@@NULL@@` 的编解码**上移到编排层**，存储层只认 payload。所有异常捕获后记日志并返回"未命中/失败"，保持降级语义与现状一致。

> 设计取舍：存储层返回 `byte[]` 而非 `string`，是为了让 MemoryPack 等二进制序列化器不需要走 Base64/UTF8 假字符串，消除现有 `SerializeToString` 对二进制序列化器的不友好。

### 3.2 策略层（`L2Cache.Abstractions` 新增）

```csharp
namespace L2Cache.Abstractions.Policies;

/// <summary>Key 构建策略。替代 AbstractCacheService.BuildCacheKey 虚方法。</summary>
public interface IKeyBuilder<TKey> where TKey : notnull
{
    string Build(TKey key);
}

/// <summary>默认实现：string/ValueType 直接 ToString()，复杂类型抛异常（与现状一致）。</summary>
public sealed class DefaultKeyBuilder<TKey> : IKeyBuilder<TKey> where TKey : notnull;

/// <summary>过期策略。入参为显式传入的 TTL（可为 null）。</summary>
public interface IExpiryPolicy
{
    TimeSpan ResolveL2Ttl(TimeSpan? requested);
    TimeSpan ResolveL1Ttl(TimeSpan l2Ttl);
}

/// <summary>锁策略。封装"内存锁 + 分布式锁"的获取/释放与双检查节奏。</summary>
public interface ILockPolicy
{
    /// <summary>尝试获取锁。返回 null 表示"未获取到但允许降级直读/直写"（对应现有降级语义）。</summary>
    ValueTask<CacheLockHandle?> AcquireAsync(string resourceKey, CancellationToken ct = default);
}

public interface ICacheLockHandle : IAsyncDisposable
{
    bool Acquired { get; }
}

/// <summary>空值策略：是否缓存空值、空值的哨兵编解码、空值 TTL。</summary>
public interface INullValuePolicy
{
    bool Enabled { get; }
    TimeSpan Ttl { get; }
    bool IsNullSentinel(in StoreEntry entry, out ReadOnlyMemory<byte> remainder);
    ReadOnlyMemory<byte> CreateSentinel();
}

/// <summary>回源委托。替代 QueryDataAsync/QueryDataListAsync 虚方法。</summary>
public interface ILoader<TKey, TValue> where TKey : notnull
{
    Task<TValue?> LoadAsync(TKey key, CancellationToken ct = default);
    /// <summary>默认实现：逐 Key 调用 LoadAsync。可覆写为真正的批量回源。</summary>
    Task<Dictionary<TKey, TValue>> LoadManyAsync(IReadOnlyList<TKey> keys, CancellationToken ct = default);
}
```

**内置实现**（随 Core 提供，开箱即用）：

| 策略 | 内置实现 | 行为 |
|------|---------|------|
| `IExpiryPolicy` | `DefaultExpiryPolicy` | L1 TTL = min(L2 TTL, 配置的 `MaxL1Ttl`，默认 5 分钟)，等价现状但上限可配置 |
| `ILockPolicy` | `NoLockPolicy` / `MemoryLockPolicy` / `DistributedLockPolicy` / `ChainedLockPolicy`（内存→分布式） | 分布式锁实现抽离自旋等待逻辑，退避参数可配置；后续可替换 RedLock |
| `INullValuePolicy` | `SentinelNullValuePolicy` | 保留 `@@NULL@@` 字符串哨兵，兼容旧数据 |
| `IKeyBuilder` | `DefaultKeyBuilder` | 与现状一致 |

### 3.3 区域描述符与配置

```csharp
namespace L2Cache.Configuration;

/// <summary>一个缓存区域的全部配置。由 AddCache&lt;TKey,TValue&gt;(name, configure) 生成。</summary>
public sealed class CacheRegionOptions
{
    public string CacheName { get; internal set; } = "";       // 区域名，决定 Redis Key 前缀与 Pub/Sub 频道
    public TimeSpan? DefaultTtl { get; set; }                  // 默认 L2 TTL；null = 永不过期（与现状一致）
    public TimeSpan MaxL1Ttl { get; set; } = TimeSpan.FromMinutes(5);
    public LockOptions Lock { get; set; } = new();             // 从全局 L2CacheOptions.Lock 作为初始值
    public NullValueOptions NullValue { get; set; } = new();
    public InvalidationOptions Invalidation { get; set; } = new(); // 是否参与 Pub/Sub、是否作为发布方
    public Func<IKeyBuilder<TKey>>? KeyBuilderFactory;         // 内部使用，见 3.5 注册 API
}

/// <summary>全局配置（瘦身后）。</summary>
public sealed class L2CacheOptions
{
    public bool UseLocalCache { get; set; } = true;
    public bool UseRedis { get; set; }
    public RedisCacheOptions Redis { get; set; } = new();      // 连接串、Database、可选的 ConfigurationOptions 定制
    public TelemetryOptions Telemetry { get; set; } = new();
    public BackgroundRefreshOptions BackgroundRefresh { get; set; } = new();
    /// <summary>默认区域配置，AddCache 未显式覆盖的项从此继承。</summary>
    public Action<CacheRegionOptions>? DefaultRegion { get; set; }
}
```

> 注意：`Lock` / `NullValue` 从全局配置**移动**到区域配置，全局 `L2CacheOptions` 上原有的同名属性标记 `[Obsolete]` 并继续生效一个版本（作为所有区域的默认值），保证二进制/源码兼容。

### 3.4 编排层（`CacheOrchestrator`）

```csharp
namespace L2Cache.Core;

/// <summary>区域运行时描述符：由注册 API 构建并冻结（含策略实例），单例注册。</summary>
public sealed class CacheDescriptor<TKey, TValue> where TKey : notnull
{
    public required string CacheName { get; init; }
    public required CacheRegionOptions Options { get; init; }
    public required IKeyBuilder<TKey> KeyBuilder { get; init; }
    public required IExpiryPolicy Expiry { get; init; }
    public required ILockPolicy Lock { get; init; }
    public required INullValuePolicy NullValue { get; init; }
    public ILoader<TKey, TValue>? Loader { get; init; }   // null = 纯缓存模式，GetOrLoad 退化为 Get
}

/// <summary>核心编排器。单例、非泛型；所有方法接收描述符。</summary>
public sealed class CacheOrchestrator
{
    public CacheOrchestrator(
        IL1CacheStore? l1,                // UseLocalCache=false 时为 null
        IL2CacheStore? l2,                // UseRedis=false 时为 null
        ICacheInvalidationBus? invalidationBus,
        ITelemetryProvider telemetry,     // 必有（默认 NoOp）
        ILogger<CacheOrchestrator> logger);

    // 读管道
    public Task<CacheResult<TValue>> GetAsync<TValue>(ICacheRegion region, string fullKey, Func<string, Task<StoreEntry>> l2Get, ...);

    // 说明：实际签名以泛型方法 + descriptor 参数为主，此处省略完整泛型签名；
    // 公开语义为下表所列管道步骤，与现 AbstractCacheService 一一对应。
}
```

**管道步骤映射**（与现有代码逐条对应，作为验收对照表）：

| 现有方法（AbstractCacheService） | 新编排方法 | 管道要点 |
|---|---|---|
| `GetInternalAsync` | `ReadPipeline` | L1→L2→(可回填 L1)；空值哨兵解码；遥测 hit/miss/error 与现状 tag 一致 |
| `GetOrLoadAsync` | `GetOrLoadPipeline` | 无锁首查→内存锁→双查→分布式锁→三查→`ILoader.LoadAsync`→回填/空值缓存；锁获取失败降级直读语义保持 |
| `PutAsync` / `InternalPutAsync` | `WritePipeline` | 锁→L2 SET→L1 SET→发布失效消息（仅当区域启用 Invalidation.Publish） |
| `EvictAsync` | `EvictPipeline` | L1 Remove→L2 Delete→发布失效消息 |
| `BatchGetAsync` | `BatchReadPipeline` | L1 命中收集→MGET 缺失 Key→回填 L1 |
| `BatchGetOrLoadAsync` | `BatchGetOrLoadPipeline` | **修复 P9**：空值命中（FoundNull）不再进入回源名单，直接在结果中返回 default |
| `BatchPutAsync` / `BatchPutInternalAsync` | `BatchWritePipeline` | Pipeline 写 L2（successKeys）→0 超时内存锁竞争写 L1→发布失效 |
| `BatchEvictAsync` | `BatchEvictPipeline` | L1 Remove→DEL→发布失效 |

**空值表示的内部修正**：L1 中的 `NullValObj`（`object` 哨兵）改为统一的 `SentinelPayload`（byte 载荷 + `INullValuePolicy` 识别），`CacheResult<TValue>` 显式区分 `Found / FoundNull / NotFound`，消灭 `default(TValue)` 与"真实 null 值"的歧义。

### 3.5 失效总线（修复 P3 / P4 / P7）

```csharp
namespace L2Cache.Abstractions.Invalidation;

/// <summary>失效消息。带版本号，消费方可据此丢弃乱序消息。</summary>
public readonly record struct InvalidationMessage(
    string CacheName,
    string Key,           // 不含区域前缀的业务 Key
    long Version);        // 单调递增（每节点基于 Environment.TickCount64 起步）

/// <summary>失效总线抽象。默认实现为 Redis Pub/Sub；可替换为 Kafka/RabbitMQ 等。</summary>
public interface ICacheInvalidationBus
{
    Task PublishAsync(InvalidationMessage message, CancellationToken ct = default);
    /// <summary>由 InvalidationSubscriber（HostedService）调用一次；重复调用返回失败。</summary>
    Task SubscribeAsync(Func<InvalidationMessage, Task> handler, CancellationToken ct = default);
}
```

- **发布端**：`CacheOrchestrator.WritePipeline/EvictPipeline` 在 L2 写入/删除成功后发布。版本号由每节点 `Interlocked.Increment` 生成，消费端记录每个 (CacheName, Key) 的最大版本，**低于已见版本的消息直接丢弃**（缓解乱序/重复）。
- **订阅端**：新的 `InvalidationSubscriber : BackgroundService`，单例注册，进程启动时订阅 `l2cache:sync:{CacheName}`（频道名取自**区域描述符的 CacheName**，修复 P4）。收到消息后按 CacheName 路由到对应 `MemoryCacheStore.Remove`。
- **兜底（P7）**：`MaxL1Ttl` 从硬编码 5 分钟改为区域配置；文档明确"Pub/Sub 丢消息时最脏 5 分钟"的语义边界。
- **兼容**：消息负载从纯 Key 字符串改为 JSON（`InvalidationMessage`）。**过渡期**：订阅端同时识别旧格式（纯字符串，Version=0）与新格式，一个版本后移除旧格式解析。

### 3.6 门面与注册 API（修复 P2 / P6）

```csharp
namespace L2Cache;

/// <summary>面向业务的新门面。Scoped；构造函数注入，无服务定位器。</summary>
public interface ICacheClient<TKey, TValue> where TKey : notnull
{
    Task<TValue?> GetAsync(TKey key, CancellationToken ct = default);
    Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(TKey key, CancellationToken ct = default);
    Task<TValue> PutAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task UpdateAsync(TKey key, TValue value, CancellationToken ct = default);       // 需要 ILoader
    Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> EvictAsync(TKey key, CancellationToken ct = default);
    Task<Dictionary<TKey, TValue>> BatchGetAsync(IReadOnlyList<TKey> keys, CancellationToken ct = default);
    Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(IReadOnlyList<TKey> keys, TimeSpan? expiry = null, CancellationToken ct = default);
    Task BatchPutAsync(IReadOnlyDictionary<TKey, TValue> data, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<long> BatchEvictAsync(IReadOnlyList<TKey> keys, CancellationToken ct = default);
}

/// <summary>内部实现：仅做 TKey→fullKey 与描述符解析，全部转发 CacheOrchestrator。约 100 行。</summary>
internal sealed class CacheClient<TKey, TValue> : ICacheClient<TKey, TValue> where TKey : notnull;
```

**注册 API（流式构建器）**：

```csharp
// Program.cs — 全局一次
builder.Services.AddL2Cache(options =>
{
    options.UseRedis = true;
    options.Redis.ConnectionString = cs;
})
// 每个缓存区域一次（通常封装在扩展方法里）
.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(10);
    region.Lock.EnabledDistributedLock = true;
})
.AddCache<string, UserDto>("users", region =>
{
    region.NullValue.Enabled = false;
});
```

构建器内部为每个区域注册：

1. `CacheDescriptor<TKey,TValue>`（单例，策略在此冻结）；
2. 可选的 `ILoader<TKey,TValue>`（由用户提供：`.WithLoader<TLoader>()` 或委托 `.WithLoader((sp, key) => ...)`）；
3. `ICacheClient<TKey,TValue>`（Scoped，构造函数仅注入 descriptor + orchestrator + loader）；
4. 可选的后台刷新：`.WithBackgroundRefresh(policy)` 注册对应的 `CacheRefreshBackgroundService<TKey,TValue>`（内部改为驱动 Orchestrator 的 `ReloadPipeline`）。

**旧 API 兼容**：

- `AbstractCacheService<TKey,TValue>`：保留全部 `public`/`protected` 签名。`GetRedisDatabase()`/`GetLocalCache()` 等基础设施虚方法标记 `[Obsolete]`；`GetInternalAsync` 等管道方法改为调用 `CacheOrchestrator`。用户自定义子类（覆写 `QueryDataAsync` 等）无需修改即可继续工作。
- `L2CacheService<TKey,TValue>`：构造函数签名不变（保持 DI 兼容），内部从"自己解析依赖"改为"注入 Orchestrator 并转发"；删除 `static IsSubscribed` 订阅逻辑（P3），订阅统一走 `InvalidationSubscriber`。其 `_cacheName` 改为调用 `GetCacheName()` 虚方法（修复 P4 的半边：新代码用描述符，旧代码覆写后行为也正确）。
- `ICacheService<TKey,TValue>` 接口不变。

### 3.7 可观测性

- `ITelemetryProvider` 接口与 tag 常量**保持不变**，`DefaultTelemetryProvider` / `DefaultHealthChecker` 不动。
- 变化：遥测调用点从 `AbstractCacheService` 各方法移到 `CacheOrchestrator` 管道中，埋点位置与 tag 保持一致（现有 tracing 单测 `AbstractCacheServiceTracingTests` 直接作为回归基线）。
- 新增指标：`l2cache.invalidation.published/received/dropped`（dropped = 版本过期被丢弃的消息）。

### 3.8 包布局调整

```
src/
├── L2Cache.Abstractions/        # 现有接口 + 新增 Policies/Invalidation 命名空间
├── L2Cache.Core/                # 新增：CacheOrchestrator、CacheClient、Stores、InvalidationBus、InvalidationSubscriber
├── L2Cache/                     # 保留：旧 AbstractCacheService/L2CacheService 转发层 + 现有 AddL2Cache 扩展（依赖 Core）
├── L2Cache.Serializers.Json/    # 不变
├── L2Cache.Serializers.MemoryPack/ # 不变（受益于 byte[] 存储）
└── L2Cache.Telemetry/           # 不变（新增失效指标）
```

> 备选：把 Core 并入现有 `L2Cache` 包避免多一个 NuGet 包。**倾向独立包**：`L2Cache`（旧 API）依赖 `L2Cache.Core`（新 API），便于未来主版本把 `L2Cache` 收缩为纯兼容包。评审时定夺。

---

## 4. 迁移计划（三阶段 Checklist）

### 阶段一：管道层落地（不改公开行为）

- [ ] 新建 `L2Cache.Core` 项目，实现 `IL1CacheStore`/`IL2CacheStore`（Memory/Redis 实现）
- [ ] 实现策略内置实现：`DefaultExpiryPolicy`、`MemoryLockPolicy`、`DistributedLockPolicy`、`ChainedLockPolicy`、`SentinelNullValuePolicy`、`DefaultKeyBuilder`
- [ ] 实现 `CacheOrchestrator` 全部 8 条管道（对照 3.4 映射表）
- [ ] 实现 `RedisPubSubInvalidationBus` + `InvalidationSubscriber`（含新旧消息格式过渡解析、版本去重）
- [ ] 实现 `CacheClient<TKey,TValue>` + `AddCache` 构建器 API
- [ ] `AbstractCacheService`/`L2CacheService` 内部改为转发 Orchestrator，公开签名不变；移除 `static IsSubscribed`
- [ ] 现有全部单元/集成测试**不改断言**通过（这是阶段一的硬性验收标准）
- [ ] 新增测试：`CacheClient` 注册/解析、`InvalidationSubscriber` 路由与版本去重、`BatchGetOrLoad` 空值命中不回源（P9 回归）

### 阶段二：新 API 推荐 + 弃用提示

- [ ] `GetRedisDatabase()`/`GetLocalCache()`/`GetOptions()` 等基础设施虚方法标 `[Obsolete]`
- [ ] 全局 `L2CacheOptions.Lock/NullValue` 属性标 `[Obsolete]`（映射为默认区域配置继续生效）
- [ ] 文档：Getting-Started / Advanced-Features 改推 `AddCache` + `ICacheClient`；新增迁移指南章节
- [ ] `examples` 增加新 API 示例（保留旧示例并标注"兼容写法"）
- [ ] 发布 `L2Cache.Core` 与升级后的 `L2Cache`（主次版本号 +1）

### 阶段三：主版本清理

- [ ] 移除 `AbstractCacheService` 中已弃用的基础设施虚方法
- [ ] 移除 Pub/Sub 旧消息格式解析
- [ ] 移除全局 `L2CacheOptions` 上已弃用属性
- [ ] `L2Cache` 包收缩为纯兼容层（或合并回 Core，视采用情况）

---

## 5. 风险与开放问题

| 风险 | 缓解 |
|------|------|
| 旧子类依赖 `protected` 管道方法的精确行为（如 `InternalPutAsync` 无锁语义） | 阶段一保持 `protected virtual` 签名与语义，集成测试覆盖 |
| 版本号去重依赖各节点时钟单调，重启后版本回退 | 起始值取 `Environment.TickCount64`（进程内单调即可）；乱序窗口远小于 L1 TTL 兜底 |
| `CacheClient` 新 API 与 `L2CacheService` 旧 API 并存造成用户困惑 | 文档明确"新项目用 ICacheClient，旧代码无需迁移"；两套 API 共享同一 Orchestrator，无状态分裂 |
| `IL2CacheStore` 吞异常降级 vs 用户需要感知 Redis 故障 | 存储层记录结构化日志 + 遥测 error 指标；HealthCheck 已覆盖连接状态 |

**开放问题（请评审时拍板）**：

1. Core 逻辑放独立包 `L2Cache.Core` 还是并入 `L2Cache`？
2. `ICacheClient` 是否需要与 `ICacheService` 签名完全一致（便于用户无痛切换），还是允许 `IReadOnlyList`/`CancellationToken` 等现代化调整（推荐后者）？
3. 阶段一是否顺带修复 P9（BatchGetOrLoad 空值重复回源）——属于行为变化，但更符合直觉（推荐修复并在 CHANGELOG 标注）。
