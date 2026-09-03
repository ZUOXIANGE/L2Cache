# 高级特性

本文深入介绍 L2Cache 的四大防护机制与扩展点：防击穿锁、空值缓存、后台刷新与跨节点失效同步。

## 1. 缓存击穿防护（锁合并回源）

**问题**：热点 Key 过期瞬间，大量并发请求同时未命中，全部打到数据源。

**方案**：`GetOrLoadAsync` 回源前先竞争锁，同一 Key 只有一个请求真正回源，其余请求等待后直接读取缓存结果。

两层锁协作（区域默认开启）：

| 锁 | 实现 | 作用范围 |
|----|------|----------|
| 内存锁 | 固定 1024 分段的 `SemaphoreSlim`（按 Key 哈希取锁） | 单进程内合并 |
| 分布式锁 | Redis `SET NX`（token 校验释放，自动过期防死锁） | 跨节点合并 |

行为细节：

- **等待超时降级**：等待超过 `Lock.LockTimeout`（默认 10s）后放弃锁直接回源（可用性优先），宁可少量重复回源也不让请求长时间阻塞。
- **双检**：拿到锁后先复查 L2（可能已被前一个请求回填），避免重复回源。
- **内存占用 O(1)**：分段锁固定 1024 个信号量，与 Key 基数无关；不同 Key 偶发映射同一分段只会串行化等待，不影响正确性。

## 2. 缓存穿透防护（空值缓存）

**问题**：查询不存在的数据（如恶意 ID），缓存永远未命中，请求全部打到数据源。

**方案**：启用后，回源得到 `null` 时向 L2 写入 `@@NULL@@` 哨兵值，后续查询直接命中空值并返回 `null`，不再回源。

```csharp
.AddCache<int, ProductDto>("products", region =>
{
    region.NullValue.Enabled = true;
    region.NullValue.Ttl = TimeSpan.FromSeconds(30);  // 建议较短，减小不一致窗口
});
```

注意：

- 空值哨兵也回填 L1，单机同样生效。
- 该数据随后被创建时，调用 `PutAsync`/`EvictAsync` 会正常覆盖/清除哨兵并广播失效。
- `BatchPutAsync` 传 `null` 值时同样写入哨兵，与单条行为一致。

## 3. 后台刷新（防雪崩 + 保新鲜度）

**问题**：大量 Key 同一时刻过期（雪崩），或长 TTL 数据陈旧。

**方案**：`WithBackgroundRefresh()` 启用后，后台服务周期性扫描 **L1 中活跃（近期被访问过）的 Key**，按 `Interval` 间隔自动刷新：

```csharp
.AddCache<string, AppSettingsDto>("appsettings", region =>
{
    region.DefaultTtl = TimeSpan.FromHours(6);
})
.WithLoader<SettingsLoader>()
.WithBackgroundRefresh(refresh => refresh.Interval = TimeSpan.FromMinutes(2));
```

刷新策略（优先级从高到低）：

1. **L2 命中**：直接采用 L2 最新值回填 L1（不回源，避免刷新风暴）。
2. **L2 未命中**：回源加载（`ReloadAsync` 语义）。

- 只有"活跃"Key 会被刷新（冷 Key 随其自然过期），避免后台资源浪费。
- 刷新间隔可由自定义 `ICacheRefreshPolicy<TKey, TValue>`（实现 `GetRefreshInterval(key)`）按 Key 覆盖全局/区域默认值。

## 4. 跨节点失效同步（Pub/Sub + 版本号）

多节点部署时，各节点拥有独立的 L1，某节点写入 L2 后其他节点的 L1 需要同步失效。

**流程**：

```
节点 A: PutAsync(k)
  ├─ L2 SET k
  ├─ L1 SET k (TTL ≤ MaxL1Ttl)
  └─ PUBLISH l2cache:sync:{CacheName} { cacheName, key, version }   ← source-gen 序列化
节点 B (订阅中):
  ├─ 校验消息版本（丢弃乱序/重复消息）
  ├─ 清除本地 L1 的 k
  └─ 后续读取走 L2 并回填最新值
```

- **版本号去重**：Pub/Sub 是 at-most-once 语义且消息可能乱序，消费端按版本号丢弃过期消息，防止旧值回填。
- **MaxL1Ttl 兜底**：若消息丢失（Pub/Sub 无持久化），各节点 L1 最迟在 `MaxL1Ttl`（默认 5 分钟）后自然过期，保证最终一致。
- **频道命名**：`{InvalidationChannelPrefix}:{CacheName}`（默认前缀 `l2cache:sync`），按区域隔离，消费端用 `{Prefix}:*` 模式订阅。
- **单机优化**：`region.PublishInvalidation = false` 可关闭广播，省去每写的发布开销。

## 5. 自定义策略示例

所有策略接口均可替换。以下示例：按业务规则定制 Key 格式。

```csharp
public class TenantKeyBuilder : IKeyBuilder<int>
{
    private readonly ITenantProvider _tenant;

    public TenantKeyBuilder(ITenantProvider tenant) => _tenant = tenant;

    public string Build(int key) => $"{_tenant.TenantId}:product:{key}";
}
```

```csharp
// 注册替换（核心包解析策略时优先从 DI 获取，未注册才回退默认实现；注册后全局生效）
services.Replace(ServiceDescriptor.Singleton(typeof(IKeyBuilder<int>), typeof(TenantKeyBuilder)));
```

其他可替换项见 [API 参考 · 可插拔策略接口](API-Reference.md#可插拔策略接口)。

## 6. 容错与降级汇总

| 故障场景 | 行为 |
|----------|------|
| Redis 连接失败（读） | 视为未命中，走 L1 / 回源 |
| Redis 连接失败（写） | 返回失败并记日志，不影响主流程 |
| Redis 完全不可用 | 自动退化为纯内存缓存 |
| 分布式锁获取超时 | 降级为无锁直读直写（可用性优先） |
| 失效消息丢失 | `MaxL1Ttl` 兜底过期，保证最终一致 |
| 回源数据源抛异常 | 异常冒泡给调用方（可自行熔断），不污染缓存 |
