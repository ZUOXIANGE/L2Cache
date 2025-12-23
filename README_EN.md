# L2Cache

**High-Performance .NET Distributed Second-Level Cache Framework**

[English](README_EN.md) | [中文](README.md)

L2Cache is a modern distributed second-level cache library designed for .NET applications. It seamlessly blends local in-memory cache (L1) and Redis distributed cache (L2) to provide lightning-fast response capabilities and ultimate system reliability for high-concurrency applications.

[![CI](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![License](https://img.shields.io/github/license/ZUOXIANGE/L2Cache)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/ZUOXIANGE/L2Cache)](https://github.com/ZUOXIANGE/L2Cache/commits/main)
[![GitHub Issues](https://img.shields.io/github/issues/ZUOXIANGE/L2Cache)](https://github.com/ZUOXIANGE/L2Cache/issues)
[![GitHub Stars](https://img.shields.io/github/stars/ZUOXIANGE/L2Cache?style=social)](https://github.com/ZUOXIANGE/L2Cache/stargazers)

---

## ✨ Key Features

- **🚀 Multi-Level Caching Architecture**
  - **L1 (Memory)**: Based on `IMemoryCache`, providing nanosecond-level data access and automatically handling hot data.
  - **L2 (Redis)**: Based on `StackExchange.Redis`, providing distributed sharing capabilities to ensure data consistency and persistence.
  - **Smart Synchronization**: Automatically handles data synchronization and eviction between L1 and L2 to ensure cache consistency across nodes.

- **🛡️ High Availability & Fault Tolerance**
  - **Fault Degradation**: Automatically degrades to pure memory mode when Redis is unavailable, ensuring uninterrupted service.
  - **Auto Reconnection**: Built-in resilient Redis disconnection and reconnection mechanism.
  - **Stampede Protection**: Supports background asynchronous refresh and cache preheating to avoid cache breakdown under high concurrency.
  - **Concurrency Control**: Built-in memory locks (SemaphoreSlim) and distributed locks (Redis Lock) effectively prevent cache stampede and concurrent write conflicts.

- **📊 Full-Link Observability**
  - **Metrics**: Based on OpenTelemetry standards, out-of-the-box Prometheus/Grafana monitoring metrics.
  - **Tracing**: Complete distributed tracing support to clearly gain insight into cache hits and penetration paths.
  - **Logging**: Structured cache operation logs.
  - **HealthCheck**: Integrated ASP.NET Core health check to monitor cache component status in real-time.

- **🔌 Flexible & Easy to Use**
  - **Out of the Box**: Simple API design, reasonable default configurations, ready to use with just a few lines of code.
  - **Cache Aside**: Recommended to use the `L2CacheService` base class, which automatically handles "cache miss backfill" logic.
  - **Pluggable**: Supports custom serialization (System.Text.Json, MemoryPack, etc.) and telemetry implementations.

## 📚 Documentation

| Document | Description |
|------|------|
| [**Getting Started**](docs/Getting-Started.md) | Integrate L2Cache into your project from scratch |
| [**Configuration Guide**](docs/Configuration-Guide.md) | Detailed explanation of all configuration options and parameters |
| [**API Reference**](docs/API-Reference.md) | Detailed description of core interfaces and classes |
| [**Architecture**](docs/structure.md) | Understand the internal design principles of L2Cache |
| [**Advanced Features**](docs/Advanced-Features.md) | Deep dive into locking mechanisms, concurrency control, and batch operations |

## 📦 Installation

Install the core package via NuGet:

```bash
dotnet add package L2Cache
```

Install extension packages as needed:

```bash
# Telemetry & Health Checks (Metrics, Tracing, HealthCheck)
dotnet add package L2Cache.Telemetry

# High-Performance Binary Serialization (MemoryPack)
dotnet add package L2Cache.Serializers.MemoryPack
```

## 🚀 Quick Start

Register services in `Program.cs`:

```csharp
using L2Cache.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL2Cache(options =>
{
    // Enable L1 Memory Cache
    options.UseLocalCache = true;
    
    // Enable L2 Redis Cache
    options.UseRedis = true;
    options.Redis.ConnectionString = builder.Configuration.GetConnectionString("Redis");

    // Enable Concurrency Locks (Optional)
    options.Lock.EnabledMemoryLock = true;
    options.Lock.EnabledDistributedLock = true;
})
.AddL2CacheTelemetry(); // Enable Telemetry
```

Define and use cache service:

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

    // Define cache name prefix
    public override string GetCacheName() => "products";
    
    // Define Key generation rule
    public override string BuildCacheKey(int id) => id.ToString();

    // Define backfill logic (called when cache miss)
    public override async Task<ProductDto?> QueryDataAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }
}
```

## 🤝 Contribution

Issues and Pull Requests are welcome!

## 📄 License

This project is licensed under the [MIT License](LICENSE).
