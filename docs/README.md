# L2Cache 文档中心

欢迎使用 L2Cache —— 高性能 .NET 多级缓存框架（L1 内存 + L2 Redis）。

## 文档导航

| 文档 | 说明 |
|------|------|
| [快速入门](Getting-Started.md) | 安装、注册、第一个缓存区域 |
| [配置指南](Configuration-Guide.md) | `L2CacheOptions`（全局）与 `CacheRegionOptions`（区域）全量参数说明 |
| [API 参考](API-Reference.md) | `ICacheClient`、`ILoader`、策略接口与扩展包 |
| [高级特性](Advanced-Features.md) | 防击穿锁、空值缓存、后台刷新、失效同步原理 |
| [架构设计](structure.md) | 内部模块划分、读写流程与设计原则 |

## 十分钟速览

```csharp
// 注册：全局配置 + 区域配置 + 回源加载器
builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;
    options.UseRedis = true;
    options.Redis.ConnectionString = "localhost:6379";
})
.AddCache<int, OrderDto>("orders", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(30);   // L2 默认 TTL
    region.NullValue.Enabled = true;                // 空值缓存防穿透
})
.WithLoader<OrderLoader>()                          // 回源逻辑
.WithBackgroundRefresh();                           // 后台刷新

// 使用：注入 ICacheClient<TKey, TValue>
public class OrderService(ICacheClient<int, OrderDto> cache)
{
    public Task<OrderDto?> GetOrderAsync(int id)
        => cache.GetOrLoadAsync(id);   // 未命中自动回源并回填 L1/L2
}
```

## 示例与测试

- **示例项目**：[examples/L2Cache.Examples](../examples/L2Cache.Examples/Program.cs) —— ASP.NET Core Web API，覆盖基础 CRUD、批量操作、自定义策略与遥测。
- **单元测试**：`tests/L2Cache.Tests.Unit`（62 个）
- **集成测试**：`tests/L2Cache.Tests.Integration`（27 个，基于 Testcontainers 自动拉起 Redis）
- **性能基准**：`benchmarks/L2Cache.Benchmarks`（BenchmarkDotNet）
