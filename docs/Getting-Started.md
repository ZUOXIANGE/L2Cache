# 快速入门

本文带你从零开始，在 .NET 应用中集成 L2Cache 并创建第一个缓存区域。

> 环境要求：.NET 10+；使用 L2 功能时需要可用的 Redis 实例。

## 1. 安装

```bash
dotnet add package L2Cache

# 可选：遥测（OpenTelemetry 指标与链路追踪）
dotnet add package L2Cache.Telemetry
```

## 2. 注册 L2Cache

在 `Program.cs` 中调用 `AddL2Cache`（全局配置），随后用 `AddCache<TKey, TValue>` 注册缓存区域（区域配置）：

```csharp
using L2Cache;
using L2Cache.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;   // L1 内存缓存
    options.UseRedis = true;        // L2 Redis 缓存
    options.Redis.ConnectionString =
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
})
.AddCache<int, ProductDto>("products", region =>
{
    // 区域配置：该区域所有 Key 的默认 TTL 与行为
    region.DefaultTtl = TimeSpan.FromMinutes(30);
    region.MaxL1Ttl = TimeSpan.FromMinutes(5);
})
.WithLoader<ProductLoader>();
```

关键概念：

- **缓存区域（Region）**：由 `AddCache<TKey, TValue>(name)` 声明的一组同类型缓存。区域名同时是 Redis Key 前缀（`{CacheName}:{Key}`）与失效频道后缀。
- **Loader（回源加载器）**：缓存未命中时的数据源。从 DI 解析，可以注入 Scoped 依赖（如 `DbContext`、仓储）。
- **生命周期**：`ICacheClient<TKey, TValue>` 注册为 Scoped，直接注入使用。

## 3. 定义回源加载器

实现 `ILoader<TKey, TValue>` 接口：

```csharp
using L2Cache.Abstractions.Policies;

public class ProductLoader : ILoader<int, ProductDto>
{
    private readonly IProductRepository _repo;

    public ProductLoader(IProductRepository repo) => _repo = repo;

    // 单条回源（GetOrLoadAsync 使用）
    public async Task<ProductDto?> LoadAsync(int key, CancellationToken cancellationToken = default)
        => await _repo.GetByIdAsync(key, cancellationToken);

    // 批量回源（BatchGetOrLoadAsync 使用）——真实场景请翻译为一条 IN 查询
    public async Task<Dictionary<int, ProductDto>> LoadManyAsync(
        IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
        => await _repo.GetByIdsAsync(keys, cancellationToken);
}
```

不需要回源的场景（纯 KV 用法，如分布式内存扩展）可以不注册 Loader，只使用 `GetAsync` / `PutAsync` / `EvictAsync`。

## 4. 注入使用

```csharp
using L2Cache.Abstractions;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ICacheClient<int, ProductDto> _cache;

    public ProductsController(ICacheClient<int, ProductDto> cache) => _cache = cache;

    // Cache-Aside：L1 -> L2 -> 回源 -> 回填 L1 + L2
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var product = await _cache.GetOrLoadAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    // 写缓存：更新 L2 与 L1，并广播失效消息
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, ProductDto dto)
    {
        await _cache.PutAsync(id, dto, TimeSpan.FromMinutes(30));
        return NoContent();
    }

    // 删缓存：移除 L1 + L2，并广播失效消息
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _cache.EvictAsync(id);
        return NoContent();
    }
}
```

## 5. 启用遥测（可选）

```csharp
builder.Services.AddL2CacheTelemetry();

// 将 L2Cache 接入 OpenTelemetry（示例）
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("L2Cache"))
    .WithTracing(tracing => tracing.AddSource("L2Cache"));
```

## 6. 运行验证

完整可运行的示例见 [examples/L2Cache.Examples](../examples/L2Cache.Examples/Program.cs)：

```bash
dotnet run --project examples/L2Cache.Examples
# 打开 http://localhost:5000/scalar/v1 查看各场景端点
```

## 下一步

- 了解全部配置项：[配置指南](Configuration-Guide.md)
- 理解锁、空值缓存与失效同步：[高级特性](Advanced-Features.md)
- 完整接口签名：[API 参考](API-Reference.md)
