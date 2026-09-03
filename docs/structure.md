# 架构设计

本文介绍 L2Cache 的内部架构、读写流程与设计原则。

## 总体架构

```
                       ┌─────────────────────────────────────────┐
                       │              应用代码                     │
                       │   ICacheClient<TKey, TValue> (Scoped)    │
                       └───────────────────┬─────────────────────┘
                                           │
                       ┌───────────────────▼─────────────────────┐
                       │        CacheOrchestrator (单例)          │
                       │   编排：读/写/删/批量 + 锁 + 遥测          │
                       └──┬──────────┬──────────┬───────────────┘
                          │          │          │
             ┌────────────▼───┐ ┌────▼────────┐ ┌▼──────────────────┐
             │ L1CacheStore   │ │ L2CacheStore│ │ ICacheInvalidationBus│
             │ (IMemoryCache) │ │ (Redis)     │ │ (Redis Pub/Sub)    │
             └────────────────┘ └─────────────┘ └────────────────────┘
                          ▲                        │
             ┌────────────┴───────────┐  ┌─────────▼──────────────┐
             │ InvalidationSubscriber │  │ ILoader<TKey,TValue>    │
             │ (清理本节点 L1)         │  │ 回源加载器 (DI Scoped)   │
             └────────────────────────┘  └────────────────────────┘
```

## 项目结构

```
src/
├── L2Cache.Abstractions/        # 接口与抽象（零依赖）
│   ├── ICacheClient.cs          #   业务门面接口 + ICacheRefreshable
│   ├── Policies/                #   ILoader / IKeyBuilder / IExpiryPolicy / ILockPolicy / INullValuePolicy
│   ├── Stores/                  #   IL1CacheStore / IL2CacheStore
│   ├── Invalidation/            #   ICacheInvalidationBus + InvalidationMessage
│   ├── Serialization/           #   ICacheSerializer
│   └── Telemetry/               #   ITelemetryProvider 及健康检查抽象
├── L2Cache/                     # 核心实现
│   ├── Core/                    #   CacheOrchestrator / CacheClient / CacheDescriptor
│   ├── Stores/                  #   MemoryCacheStore (L1) / RedisCacheStore (L2)
│   ├── Policies/                #   过期/锁/空值默认实现
│   ├── Invalidation/            #   Redis Pub/Sub 总线 + 订阅后台服务
│   ├── Background/              #   后台刷新 HostedService
│   ├── Internal/                #   分段锁、活跃 Key 跟踪
│   └── Configuration/           #   L2CacheOptions / CacheRegionOptions
├── L2Cache.Serializers.Json/    # System.Text.Json 序列化器
├── L2Cache.Serializers.MemoryPack/ # MemoryPack 序列化器
└── L2Cache.Telemetry/           # OpenTelemetry 实现 + 健康检查器
```

## 核心组件

### CacheClient（Scoped 门面）

`ICacheClient<TKey, TValue>` 的实现。持有区域 `CacheDescriptor` 与 `ILoader` 引用，将业务调用转发给 `CacheOrchestrator`。注册为 Scoped，因此 Loader 及其依赖（如 DbContext）按请求生命周期解析。

### CacheDescriptor（区域元数据）

每个区域的不可变描述：`CacheName`、KeyBuilder、ExpiryPolicy、LockPolicy、NullValuePolicy、序列化器、Key 跟踪器等。在 `AddCache(...)` 注册时构建，单例共享。

### CacheOrchestrator（单例编排器）

实现所有读写编排逻辑：两级查找、锁、回源、回填、失效广播、遥测埋点。无状态（状态都在 Store 与 Descriptor 中），线程安全。

## 读流程（GetOrLoadAsync）

```
1. L1 查找 ── 命中 ──► 返回（纳秒级）
      │ 未命中
2. 获取内存分段锁 ──► 获取分布式锁
      │
3. 双检 L2 ── 命中 ──► 回填 L1（TTL = min(L2TTL, MaxL1Ttl)）──► 返回
      │ 未命中
4. ILoader.LoadAsync(key) 回源
      │
      ├─ 非 null ──► L2 SET + L1 回填 + 失效广播 ──► 返回
      └─ null   ──► 空值缓存开启？── 是 ──► 写 @@NULL@@ 哨兵 ──► 返回 null
                              └── 否 ──► 直接返回 null（下次仍回源）
```

锁等待超时（`LockTimeout`）时降级：跳过锁直接执行 3-4 步（可用性优先）。

## 写流程（PutAsync / EvictAsync）

```
1. L2 SET/DEL（Pipeline 批量化）
2. L1 SET/DEL
3. PublishInvalidation = true 时发布失效消息（版本号 + source-gen 序列化）
```

其他节点收到消息后清除本地 L1；消息丢失时由 `MaxL1Ttl` 兜底。

## 失效同步细节

- **消息结构**：`InvalidationMessage(CacheName, Key, Version)`，`Version` 为发布方节点内的单调递增版本。
- **订阅**：`InvalidationSubscriber`（HostedService）以 `{Prefix}:*` 模式订阅，按版本号去重后清除本地 L1。
- **仅跨节点需要**：同时启用 L1 与 Redis 时才注册订阅服务；`UseRedis = false` 时无需失效总线。

## 设计原则

L2Cache 遵循的三个核心原则：

1. **组合优于继承**：拆分为门面（Client）+ 编排（Orchestrator）+ 策略（Policies），每层职责单一，无基类约束，DI 友好。
2. **区域化配置**：TTL、锁、空值等策略按区域隔离，一个应用内不同业务可以拥有完全不同的缓存行为。
3. **可靠性优先的降级链**：任何外部依赖（Redis、锁）失效都降级而不是失败——降级只损失性能增益，不损失可用性。

## 性能设计

- **热路径零浪费**：遥测未启用时无 tags 数组分配；Debug 日志带 `IsEnabled` 守卫。
- **失效消息 source-gen**：固定类型经 `InvalidationMessageJsonContext` 源生成序列化，较反射 -28% 分配（实测数据见 `benchmarks/L2Cache.Benchmarks/SerializationBenchmarks.cs`）。
- **分段锁**：固定 1024 个 `SemaphoreSlim`，内存 O(1)，与 Key 基数无关。
- **批量 Pipeline**：L2 批量读（MGET）/写/删除均通过 StackExchange.Redis 批量 API 合并网络往返。

## 测试体系

| 项目 | 数量 | 说明 |
|------|------|------|
| `tests/L2Cache.Tests.Unit` | 62 | 编排逻辑、策略、空值、Key 跟踪（Fake Store） |
| `tests/L2Cache.Tests.Integration` | 27 | 真实 Redis（Testcontainers）：L1/L2 交互、击穿、空值、失效广播、多节点 |
| `benchmarks/L2Cache.Benchmarks` | — | BenchmarkDotNet：基础操作与序列化分配对比 |
