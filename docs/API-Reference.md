# L2Cache API 参考手册

本文档涵盖了 L2Cache 的核心接口、基类和配置对象。

## 1. 核心服务接口

### `ICacheService<TKey, TValue>`

这是业务层使用的主要接口，定义了缓存的基本操作。

```csharp
public interface ICacheService<TKey, TValue> where TKey : notnull
{
    // 获取缓存，如果未命中返回默认值 (null)
    Task<TValue?> GetAsync(TKey key);
    
    // 获取缓存，如果未命中则调用回源方法加载并写入缓存 (Cache-Aside)
    Task<TValue?> GetOrLoadAsync(TKey key);
    
    // 写入缓存 (同时写入 L1 和 L2)
    Task<TValue> PutAsync(TKey key, TValue value, TimeSpan? expiry = null);

    // 仅当缓存不存在时写入 (NX 模式)
    Task<bool> PutIfAbsentAsync(TKey key, TValue value, TimeSpan? expiry = null);
    
    // 移除缓存 (同时移除 L1 和 L2)
    Task<bool> EvictAsync(TKey key);
    
    // 批量获取
    Task<Dictionary<TKey, TValue>> BatchGetAsync(List<TKey> keyList);

    // 批量获取或加载
    Task<Dictionary<TKey, TValue>> BatchGetOrLoadAsync(List<TKey> keyList, TimeSpan? expiry = null);

    // 批量移除
    Task<long> BatchEvictAsync(List<TKey> keyList);
    
    // 清空当前缓存集 (基于 CacheName)
    Task ClearAsync();
}
```

### `ICacheRefreshPolicy<TKey, TValue>`

定义缓存自动刷新策略的接口。

```csharp
public interface ICacheRefreshPolicy<TKey, TValue> where TKey : notnull
{
    // 获取指定 Key 的刷新间隔
    // 返回 null 则使用全局默认配置
    TimeSpan? GetRefreshInterval(TKey key);
}
```

## 2. 基类 (推荐使用)

### `L2CacheService<TKey, TValue>`

推荐继承此基类来实现业务缓存服务。它自动处理了依赖注入、序列化、多级缓存协调等复杂逻辑。

**核心方法**:

- `GetCacheName()`: **[必须实现]** 定义缓存名称，用于 Redis Key 前缀隔离。
- `BuildCacheKey(TKey key)`: **[必须实现]** 将强类型 Key 转换为字符串 Key。
- `QueryDataAsync(TKey key)`: **[必须实现]** 回源逻辑。当缓存未命中时调用此方法从数据库加载数据。
- `QueryDataListAsync(List<TKey> keyList)`: **[可选实现]** 批量回源逻辑。用于优化 `BatchGetOrLoadAsync` 的性能。
- `GetOrLoadAsync(TKey key)`: **[核心]** 获取或加载数据。执行流程：L1 -> L2 -> QueryDataAsync -> L2 -> L1。

**示例**:

```csharp
public class MyCacheService : L2CacheService<int, MyData>
{
    // ... 构造函数 ...
    public override string GetCacheName() => "mydata";
    public override string BuildCacheKey(int id) => id.ToString();
    public override async Task<MyData?> QueryDataAsync(int id) => await _repo.Get(id);
}
```

### `AbstractCacheService<TKey, TValue>`

底层抽象基类。如果您需要完全自定义底层依赖（例如不使用 `L2CacheOptions`，或者需要连接多个 Redis 实例），可以继承此类。

## 3. 序列化接口

### `ICacheSerializer`

定义数据的序列化与反序列化行为。

```csharp
public interface ICacheSerializer
{
    string Name { get; }
    
    byte[] Serialize<T>(T value);
    T? Deserialize<T>(byte[] data);
    
    // ... 其他辅助方法
}
```

**内置实现**:

- `JsonCacheSerializer`: 基于 `System.Text.Json` (默认)。
- `MemoryPackCacheSerializer`: 基于 `MemoryPack` (需引入扩展包)。

## 4. 配置类

### `L2CacheOptions`

详情请参阅 [配置指南](Configuration-Guide.md)。
