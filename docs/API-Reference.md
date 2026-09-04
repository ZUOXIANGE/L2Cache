# API 参考

## ICacheClient\<TKey, TValue\>

多级缓存（L1 + L2）的统一业务门面，注入即用。命名空间：`L2Cache.Abstractions`。

```csharp
public interface ICacheClient<TKey, TValue> where TKey : notnull
{
    string CacheName { get; }

    // ---- 单条操作 ----
    Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);
    Task<TValue?> GetOrLoadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(TKey key, CancellationToken cancellationToken = default);
    Task PutAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task<bool> EvictAsync(TKey key, CancellationToken cancellationToken = default);
    Task<TValue?> ReloadAsync(TKey key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    // ---- 批量操作 ----
    Task<Dictionary<TKey, TValue>> BatchGetAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);
    Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(IReadOnlyList<TKey> keys, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task BatchPutAsync(IReadOnlyDictionary<TKey, TValue> data, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task<long> BatchEvictAsync(IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);
}
```

### 方法说明

| 方法 | 行为 |
|------|------|
| `GetAsync` | 纯缓存查询（L1 → L2，L2 命中回填 L1），不回源；未命中返回 `default` |
| `GetOrLoadAsync` | Cache-Aside 主入口：未命中时经 `ILoader` 回源并回填 L1/L2；回源为 `null` 且启用空值缓存时写入空值哨兵 |
| `ExistsAsync` | L1 或 L2 任一存在即 `true` |
| `PutAsync` | 覆盖写：L2 SET + L1 回填 + 失效广播 |
| `PutIfAbsentAsync` | NX 语义，仅当 L2 不存在时写入，返回是否成功 |
| `EvictAsync` | 移除 L1 + L2 并广播失效；返回 L2 是否确有删除 |
| `ReloadAsync` | 强制回源并覆盖缓存（跳过缓存读取） |
| `BatchGetAsync` | L2 MGET 批量查询 + L1 回填，仅返回命中的非空值 |
| `BatchGetOrLoadAsync` | 未命中 Key 经 `ILoader.LoadManyAsync` 批量回源；空值 Key 不重复回源 |
| `BatchPutAsync` | Pipeline 批量写入（支持值中含 `null`，走空值哨兵） |
| `BatchEvictAsync` | Pipeline 批量删除 + 失效广播，返回成功删除数 |

> `GetAsync` 与 `GetOrLoadAsync` 职责分离：只读兜底场景用前者（不会意外触发回源），典型读场景用后者。

## ILoader\<TKey, TValue\>

回源加载器。命名空间：`L2Cache.Abstractions.Policies`。

```csharp
public interface ILoader<TKey, TValue> where TKey : notnull
{
    /// <summary>单条回源。返回 null 表示数据源无此数据。</summary>
    Task<TValue?> LoadAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>批量回源。建议实现为一条 IN 查询；未命中的 Key 不放入返回字典。</summary>
    Task<Dictionary<TKey, TValue>> LoadManyAsync(
        IReadOnlyList<TKey> keys, CancellationToken cancellationToken = default);
}
```

- 通过 `WithLoader<TLoader>()`（类型，从 DI 解析）或 `WithLoader(factory)`（工厂）注册。
- Loader 生命周期与解析作用域由 DI 决定，**可安全依赖 Scoped 服务**（如 `DbContext`）。

## 注册与构建器

命名空间：`L2Cache`（`ServiceCollectionExtensions`）。

```csharp
public static IL2CacheBuilder AddL2Cache(
    this IServiceCollection services, Action<L2CacheOptions> configure);

public interface IL2CacheBuilder
{
    IServiceCollection Services { get; }
    IL2CacheRegionBuilder<TKey, TValue> AddCache<TKey, TValue>(
        string cacheName, Action<CacheRegionOptions>? configure = null) where TKey : notnull;
}

public interface IL2CacheRegionBuilder<TKey, TValue> where TKey : notnull
{
    IServiceCollection Services { get; }
    IL2CacheRegionBuilder<TKey, TValue> WithLoader<TLoader>() where TLoader : class, ILoader<TKey, TValue>;
    IL2CacheRegionBuilder<TKey, TValue> WithLoader(Func<IServiceProvider, ILoader<TKey, TValue>> loaderFactory);
    IL2CacheRegionBuilder<TKey, TValue> WithBackgroundRefresh(Action<BackgroundRefreshOptions>? configure = null);
}
```

## 后台刷新（ICacheRefreshable\<TKey\>）

`WithBackgroundRefresh()` 启用后，后台服务会调度刷新 L1 中活跃的 Key：

```csharp
public interface ICacheRefreshable<TKey> where TKey : notnull
{
    Task RefreshKeyAsync(TKey key, CancellationToken cancellationToken = default);
}
```

刷新优先读取 L2 最新值（避免回源风暴），L2 亦未命中时才回源。刷新间隔由 `BackgroundRefreshOptions.Interval` 或 `ICacheRefreshPolicy` 决定。

## 可插拔策略接口

所有策略均可替换（`services.Replace(...)` 或传入 `CacheDescriptor` 对应实现）：

| 接口 | 默认实现 | 职责 |
|------|----------|------|
| `IKeyBuilder<TKey>` | `DefaultKeyBuilder<TKey>` | 业务 Key → 字符串缓存 Key（string/枚举/基元类型直接 `ToString()`；复杂类型抛异常，要求自定义实现） |
| `IExpiryPolicy` | `DefaultExpiryPolicy` | TTL 解析：显式 expiry → `DefaultTtl` → `NullValue.Ttl`；L1 = min(L2, `MaxL1Ttl`) |
| `ILockPolicy` | `MemoryLockPolicy` + `DistributedLockPolicy`（链式组合为 `ChainedLockPolicy`） | 回源合并与并发写控制 |
| `INullValuePolicy` | `SentinelNullValuePolicy` | 空值哨兵（`@@NULL@@`）的写入与识别 |
| `ICacheSerializer` | `JsonCacheSerializer`（Json 包） / `MemoryPackCacheSerializer`（MemoryPack 包） | 值 ↔ UTF-8 字节 |
| `ICacheInvalidationBus` | `RedisPubSubInvalidationBus` | 失效消息广播（可替换为 Kafka/RabbitMQ 等） |
| `ITelemetryProvider` | `NoOpTelemetryProvider`（核心包） / `DefaultTelemetryProvider`（Telemetry 包） | Tracing/Metrics/日志埋点 |

## 序列化扩展包

- **L2Cache.Serializers.Json**：System.Text.Json，UTF-8，默认 CamelCase + 忽略 null + 中文友好编码。任意泛型类型均支持（反射元数据由 STJ 内部缓存）。
- **L2Cache.Serializers.MemoryPack**：高性能二进制序列化，适合大对象与极致吞吐场景。

自定义序列化器只需实现 `ICacheSerializer` 并注册替换：

```csharp
services.Replace(ServiceDescriptor.Singleton<ICacheSerializer, MySerializer>());
```

## 遥测（L2Cache.Telemetry 包）

```csharp
builder.Services.AddL2CacheTelemetry();
```

- `ITelemetryProvider`：`StartActivity`（Tracing）、`RecordCacheHit/Miss/Write/...`（Metrics 直方图与计数）、异常埋点。
- 未安装 Telemetry 包时使用 NoOp 实现，全部调用为空操作且不产生分配。

## L2 存储容错语义

`RedisCacheStore` 的所有操作均容忍连接故障：读取失败视为未命中、写入失败返回 `false`，异常仅记录日志。上层保持降级语义——Redis 不可用时自动退化为纯内存缓存，恢复后自动重新接管。
