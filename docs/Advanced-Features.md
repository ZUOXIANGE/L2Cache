# L2Cache 高级特性

## 1. 并发控制与锁机制

在极高并发场景下，缓存系统面临两大挑战：**缓存击穿 (Cache Stampede)** 和 **并发写入冲突**。L2Cache 内置了多级锁机制来解决这些问题。

### 1.1 缓存击穿保护

当一个热点 Key 过期时，如果同时有成千上万的请求涌入，它们可能会同时发现缓存失效，然后同时去查询数据库，瞬间击垮数据库。

L2Cache 采用了 **Double-Checked Locking (双重检查锁)** 模式：

1.  **检查 L1/L2 缓存**：如果存在，直接返回。
2.  **获取锁**：如果不存在，尝试获取锁（先内存锁，后分布式锁）。
3.  **再次检查缓存**：获取锁后，再次检查缓存（可能别的线程已经加载好了）。
4.  **回源加载**：如果还是没有，才真正去查询数据库。
5.  **写入缓存并释放锁**。

### 1.2 锁的类型

-   **内存锁 (Memory Lock)**: 使用 `SemaphoreSlim` 实现。
    -   **作用**: 限制单机内只有一个线程能去加载同一个 Key。
    -   **性能**: 极高，无网络开销。
    -   **局限**: 无法限制多实例/多节点间的并发。

-   **分布式锁 (Distributed Lock)**: 使用 Redis `SET NX` 实现。
    -   **作用**: 限制整个集群内只有一个实例能去加载同一个 Key。
    -   **性能**: 依赖 Redis 网络往返，略低于内存锁。
    -   **互补**: L2Cache 默认先抢内存锁，抢到的那个线程再去抢分布式锁，从而将集群级别的竞争降低到单机级别的竞争，极大减少了 Redis 的锁争用压力。

### 1.3 启用方式

请参考 [配置指南](Configuration-Guide.md#6-并发锁配置-lock-options)。

## 2. 批量操作优化

L2Cache 针对批量场景（如列表页加载）进行了深度优化。

### 2.1 批量获取 (BatchGet)

使用 Redis `MGET` 命令一次性获取多个 Key，大幅减少网络 RTT。

### 2.2 批量回源与僵尸缓存防御

`BatchGetOrLoadAsync` 实现了智能的批量回源逻辑：

1.  **批量查询缓存**：先尝试从 L1/L2 批量获取。
2.  **计算缺失 Key**：找出未命中的 Key。
3.  **批量回源**：调用用户实现的 `QueryDataListAsync` 一次性从 DB 加载。
4.  **安全回填 (Anti-Zombie)**：
    -   在将 DB 数据回填到缓存之前，L2Cache 会对每个 Key 进行**版本/存在性检查**。
    -   如果在此期间有并发的 `PutAsync` 更新了某个 Key，批量回填逻辑会**放弃**覆盖该 Key，从而避免将旧数据覆盖新数据（僵尸缓存问题）。

## 3. 缓存一致性保障

L2Cache 遵循 **Cache Aside** 模式，并提供以下机制保障一致性：

-   **更新策略**: `PutAsync` 会同时更新 L1 和 L2。
-   **淘汰策略**: `EvictAsync` 会同时删除 L1 和 L2。
-   **自动过期**: 支持 TTL (Time To Live)。
-   **被动更新**: 可通过扩展 `OnRedisCacheSet` 结合 Redis Pub/Sub 实现多级缓存同步（参见 [4.2 OnRedisCacheSet](#42-onrediscacheset)），或依赖被动过期。
-   **强制刷新**: `ReloadAsync` 强制回源并更新缓存。

## 4. 扩展点与回调 (Hooks)

L2Cache 提供了受保护的虚方法（Hooks），允许开发者在缓存写入时注入自定义逻辑。您可以通过继承 `L2CacheService<TKey, TValue>` 并重写这些方法来实现。

### 4.1 OnLocalCacheSet

当数据被写入本地缓存（L1）时触发。

- **签名**: `protected virtual void OnLocalCacheSet(TKey key, TValue value)`
- **触发时机**: `PutAsync`、`GetOrLoadAsync` (回填 L1)、`BatchGetOrLoadAsync` (回填 L1) 等所有写入 L1 的操作。
- **典型用途**:
    -   **后台刷新**: L2Cache 默认实现利用此钩子将 Key 加入 `CacheKeyTracker`，以支持后台自动刷新。
    -   **辅助索引**: 维护一个本地的 Key 集合，用于模糊查询或批量管理。
    -   **本地监控**: 记录本地缓存的变更频率。

**示例代码**:

```csharp
public class MyCustomCacheService : L2CacheService<string, User>
{
    // 构造函数省略...

    protected override void OnLocalCacheSet(string key, User value)
    {
        base.OnLocalCacheSet(key, value); // 重要：保留默认的后台刷新逻辑
        
        // 自定义逻辑
        Console.WriteLine($"[L1 Hook] Key {key} updated in local cache.");
    }
}
```

### 4.2 OnRedisCacheSet

当数据被写入 Redis 缓存（L2）时触发。

- **签名**: `protected virtual void OnRedisCacheSet(TKey key, TValue value, TimeSpan? expiry)`
- **触发时机**: `PutAsync` 写入 Redis 成功后。
- **典型用途**:
    -   **缓存同步**: 发送 Redis Pub/Sub 消息，通知其他节点清除该 Key 的本地缓存（实现即时一致性）。
    -   **审计日志**: 记录关键数据的变更历史。
    -   **二级索引**: 在 Redis 中更新该数据对应的索引（如 Set 或 ZSet）。

**示例代码**:

```csharp
protected override void OnRedisCacheSet(string key, User value, TimeSpan? expiry)
{
    // 自定义逻辑：发送变更通知
    var database = GetRedisDatabase();
    database?.PublishAsync("cache-invalidation-channel", key);
}
```
