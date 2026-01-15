# L2Cache

**高性能 .NET 分布式二级缓存框架**

[English](README_EN.md) | [中文](README.md)

L2Cache 是一个为 .NET 应用程序设计的现代化分布式二级缓存库。它无缝融合了本地内存缓存 (L1) 和 Redis 分布式缓存 (L2)，旨在为高并发应用提供极速响应能力和极致的系统可靠性。

[![CI](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![License](https://img.shields.io/github/license/ZUOXIANGE/L2Cache)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/ZUOXIANGE/L2Cache)](https://github.com/ZUOXIANGE/L2Cache/commits/main)
[![GitHub Issues](https://img.shields.io/github/issues/ZUOXIANGE/L2Cache)](https://github.com/ZUOXIANGE/L2Cache/issues)
[![GitHub Stars](https://img.shields.io/github/stars/ZUOXIANGE/L2Cache?style=social)](https://github.com/ZUOXIANGE/L2Cache/stargazers)

---

## ✨ 核心特性

- **🚀 多级缓存架构**
  - **L1 (内存)**: 基于 `IMemoryCache`，提供纳秒级数据访问，自动处理热点数据。
  - **L2 (Redis)**: 基于 `StackExchange.Redis`，提供分布式共享能力，确保数据一致性与持久化。
  - **Pub/Sub 实时同步**: 利用 Redis 发布/订阅机制，当 L2 缓存更新时，实时通知所有节点清除对应的 L1 缓存，确保集群数据强一致性。

- **⚡ 高性能操作**
  - **批量操作**: 支持 `BatchGet`, `BatchPut`, `BatchEvict` 等批量 API，底层使用 Pipeline 减少网络往返 (RTT)，大幅提升吞吐量。
  - **序列化扩展**: 支持 System.Text.Json (默认) 和 MemoryPack 等高效二进制序列化。

- **🛡️ 高可用与容错**
  - **故障降级**: Redis 不可用时自动降级为纯内存模式，保障服务不中断。
  - **自动重连**: 内置弹性的 Redis 断线重连机制。
  - **防雪崩机制**: 支持后台异步刷新和缓存预热，避免高并发下的缓存击穿。
  - **并发控制**: 内置内存锁 (SemaphoreSlim) 和分布式锁 (Redis Lock)，有效防止缓存击穿 (Cache Stampede) 和并发写入冲突。

- **📊 全链路可观测性**
  - **Metrics**: 基于 OpenTelemetry 标准，开箱即用的 Prometheus/Grafana 监控指标。
  - **Tracing**: 完整的分布式链路追踪支持，清晰洞察缓存命中与穿透路径。
  - **Logging**: 结构化的缓存操作日志。
  - **HealthCheck**: 集成 ASP.NET Core 健康检查，实时监控缓存组件状态。

- **🔌 灵活易用**
  - **开箱即用**: 简洁的 API 设计，合理的默认配置，几行代码即可接入。
  - **Cache Aside**: 推荐使用 `L2CacheService` 基类，自动处理“缓存缺失回源”逻辑。
  - **插件化**: 支持自定义序列化（System.Text.Json, MemoryPack 等）和遥测实现。

## 📚 文档中心

| 文档 | 说明 |
|------|------|
| [**快速入门**](docs/Getting-Started.md) | 从零开始集成 L2Cache 到您的项目中 |
| [**配置指南**](docs/Configuration-Guide.md) | 详解所有配置选项与参数 |
| [**API 参考**](docs/API-Reference.md) | 核心接口与类的详细说明 |
| [**架构设计**](docs/structure.md) | 了解 L2Cache 的内部设计原理 |
| [**高级特性**](docs/Advanced-Features.md) | 深入了解锁机制、并发控制与批量操作 |

## 📦 安装

通过 NuGet 安装核心包：

```bash
dotnet add package L2Cache
```

根据需要安装扩展包：

```bash
# 遥测与健康检查 (Metrics, Tracing, HealthCheck)
dotnet add package L2Cache.Telemetry

# 高性能二进制序列化 (MemoryPack)
dotnet add package L2Cache.Serializers.MemoryPack
```

## 🚀 快速上手

在 `Program.cs` 中注册服务：

```csharp
using L2Cache.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL2Cache(options =>
{
    // 启用 L1 内存缓存
    options.UseLocalCache = true;
    
    // 启用 L2 Redis 缓存
    options.UseRedis = true;
    options.Redis.ConnectionString = builder.Configuration.GetConnectionString("Redis");

    // 启用并发锁 (可选)
    options.Lock.EnabledMemoryLock = true;
    options.Lock.EnabledDistributedLock = true;
    
    // 启用 Pub/Sub 消息订阅 (可选)
    options.PubSub.Enabled = true;
})
.AddL2CacheTelemetry(); // 启用遥测
```

定义并使用缓存服务：

```csharp
public class ProductCacheService : L2CacheService<int, ProductDto>
{
    private readonly IProductRepository _repo;

    public ProductCacheService(
        IServiceProvider sp, 
        IOptions<L2CacheOptions> opts, 
        ILogger<L2CacheService<int, ProductDto>> logger,
        IProductRepository repo) 
        : base(sp, opts, logger)
    {
        _repo = repo;
    }

    // 定义缓存名称前缀
    public override string GetCacheName() => "products";
    
    // 定义 Key 的生成规则
    public override string BuildCacheKey(int id) => id.ToString();

    // 定义回源逻辑 (缓存未命中时调用)
    public override async Task<ProductDto?> QueryDataAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }
}
```

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。
