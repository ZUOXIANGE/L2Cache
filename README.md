# L2Cache

**高性能 .NET 多级缓存框架**

[English](README_EN.md) | [中文](README.md)

L2Cache 是一个为 .NET 应用程序设计的现代化多级缓存库。它无缝融合本地内存缓存（L1）与 Redis 分布式缓存（L2），通过 **区域化配置** 与 **可插拔策略** 为高并发应用提供极速响应能力与极致的系统可靠性。

[![CI](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![License](https://img.shields.io/github/license/ZUOXIANGE/L2Cache)](LICENSE)

---

## ✨ 核心特性

- **🚀 多级缓存架构**
  - **L1（内存）**：基于 `IMemoryCache`，纳秒级访问，TTL 上限作为 Pub/Sub 丢消息时的一致性兜底。
  - **L2（Redis）**：基于 `StackExchange.Redis`，分布式共享，所有操作容忍连接故障（失败降级为未命中/纯内存模式）。
  - **Pub/Sub 失效同步**：L2 变更实时广播（带版本号去重），各节点清除对应 L1 缓存。

- **🧩 区域化配置 + 可插拔策略**
  - 每个缓存区域（`AddCache<TKey, TValue>(name, ...)`）独立拥有 TTL、锁、空值缓存、失效广播等配置。
  - 策略接口全部可替换：Key 构建（`IKeyBuilder`）、过期（`IExpiryPolicy`）、锁（`ILockPolicy`）、空值（`INullValuePolicy`）、序列化（`ICacheSerializer`）、失效总线（`ICacheInvalidationBus`）、遥测（`ITelemetryProvider`）。

- **⚡ 高性能设计**
  - **组合优于继承**：无基类约束，注入 `ICacheClient<TKey, TValue>` 即用；回源逻辑通过 `ILoader` 解耦，天然支持 Scoped 依赖（如 DbContext）。
  - **批量操作 Pipeline 优化**：`BatchGet`/`BatchPut`/`BatchEvict` 底层 Pipeline 合并网络往返。
  - **后台刷新**：活跃 Key 按间隔自动刷新（优先采用 L2 最新值，避免回源风暴）。
  - **零浪费热路径**：遥测未启用时无 tags 分配；固定类型失效消息使用 source-gen 序列化（较反射 -28% 分配）。

- **🛡️ 开箱即用的缓存防护**
  - **防击穿**：内存分段锁 + 分布式锁合并回源；锁超时自动降级为无锁直读（可用性优先）。
  - **防穿透**：可选空值缓存（`@@NULL@@` 哨兵 + 独立 TTL）。
  - **防雪崩**：后台刷新 + TTL 上限兜底。

- **📊 全链路可观测性**
  - OpenTelemetry 标准的 ActivitySource（Tracing）与 Meter（Metrics）。
  - 结构化操作日志（Debug 级命中日志带 `IsEnabled` 守卫，无日志开销）。

## 📚 文档中心

| 文档 | 说明 |
|------|------|
| [**快速入门**](docs/Getting-Started.md) | 从零开始集成 L2Cache |
| [**配置指南**](docs/Configuration-Guide.md) | 全局与区域配置选项详解 |
| [**API 参考**](docs/API-Reference.md) | `ICacheClient`、`ILoader` 与策略接口说明 |
| [**高级特性**](docs/Advanced-Features.md) | 锁机制、空值缓存、后台刷新与失效同步原理 |
| [**遥测文档**](docs/Telemetry.md) | OpenTelemetry 指标/链路接入与语义 |
| [**架构设计**](docs/structure.md) | 内部架构与模块划分 |

## 📦 安装

```bash
dotnet add package L2Cache
```

按需安装扩展包：

```bash
# 遥测（OpenTelemetry Metrics/Tracing）
dotnet add package L2Cache.Telemetry

# 序列化扩展
dotnet add package L2Cache.Serializers.Json      # System.Text.Json（默认）
dotnet add package L2Cache.Serializers.MemoryPack # 高性能二进制
```

## 🚀 快速上手

### 1. 注册服务

```csharp
using L2Cache;
using L2Cache.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;   // 启用 L1 内存缓存
    options.UseRedis = true;        // 启用 L2 Redis 缓存
    options.Redis.ConnectionString = builder.Configuration.GetConnectionString("Redis");
})
.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(30);
})
.WithLoader<ProductLoader>()       // 回源加载器（从 DI 解析，可注入 Scoped 依赖）
.WithBackgroundRefresh();          // 可选：后台刷新
```

### 2. 定义回源加载器

```csharp
public class ProductLoader : ILoader<int, ProductDto>
{
    private readonly IProductRepository _repo;

    public ProductLoader(IProductRepository repo) => _repo = repo;

    public async Task<ProductDto?> LoadAsync(int key, CancellationToken cancellationToken = default)
        => await _repo.GetByIdAsync(key, cancellationToken);

    // 批量回源：真实场景可翻译为一条 IN 查询
    public async Task<Dictionary<int, ProductDto>> LoadManyAsync(
        IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
        => await _repo.GetByIdsAsync(keys, cancellationToken);
}
```

### 3. 注入使用

```csharp
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ICacheClient<int, ProductDto> _cache;

    public ProductsController(ICacheClient<int, ProductDto> cache) => _cache = cache;

    [HttpGet("{id}")]
    public async Task<ProductDto?> Get(int id)
        => await _cache.GetOrLoadAsync(id);   // 未命中自动回源并回填 L1/L2

    [HttpPut("{id}")]
    public async Task Put(int id, ProductDto dto)
        => await _cache.PutAsync(id, dto);    // 写入 L1 + L2 并广播失效

    [HttpDelete("{id}")]
    public async Task Delete(int id)
        => await _cache.EvictAsync(id);       // 移除并广播失效
}
```

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！提交前请运行 `dotnet test` 确保全部通过。

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。
