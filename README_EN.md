# L2Cache

**High-Performance .NET Multi-Level Cache Framework**

[English](README_EN.md) | [中文](README.md)

L2Cache is a modern multi-level cache library designed for .NET applications. It seamlessly blends local in-memory caching (L1) with Redis distributed caching (L2), providing blazing-fast response times and extreme reliability for high-concurrency applications through **region-based configuration** and **pluggable policies**.

[![CI](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/ZUOXIANGE/L2Cache/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/L2Cache.svg)](https://www.nuget.org/packages/L2Cache)
[![License](https://img.shields.io/github/license/ZUOXIANGE/L2Cache)](LICENSE)

---

## ✨ Key Features

- **🚀 Multi-Level Cache Architecture**
  - **L1 (Memory)**: Based on `IMemoryCache` for nanosecond-level access, with a TTL ceiling as the final consistency fallback when Pub/Sub messages are lost.
  - **L2 (Redis)**: Based on `StackExchange.Redis` for distributed sharing. All operations tolerate connection failures (degrading to miss / pure-memory mode on failure).
  - **Pub/Sub Invalidation Sync**: L2 changes are broadcast in real time (with version-number deduplication), and each node clears the corresponding L1 entries.

- **🧩 Region-Based Configuration + Pluggable Policies**
  - Each cache region (`AddCache<TKey, TValue>(name, ...)`) independently owns TTL, locking, null-value caching, invalidation broadcast, and more.
  - Every policy interface is replaceable: key building (`IKeyBuilder`), expiration (`IExpiryPolicy`), locking (`ILockPolicy`), null values (`INullValuePolicy`), serialization (`ICacheSerializer`), invalidation bus (`ICacheInvalidationBus`), and telemetry (`ITelemetryProvider`).

- **⚡ High-Performance Design**
  - **Composition over Inheritance**: No base-class constraint — just inject `ICacheClient<TKey, TValue>`. Data-loading logic is decoupled via `ILoader`, which naturally supports Scoped dependencies (e.g., DbContext).
  - **Batch Pipeline Optimization**: `BatchGet`/`BatchPut`/`BatchEvict` merge network round trips via Redis Pipeline.
  - **Background Refresh**: Active keys are automatically refreshed on an interval (preferring the latest L2 value to avoid reload storms).
  - **Zero-Waste Hot Path**: No tag allocations when telemetry is disabled; fixed-type invalidation messages use source-gen serialization (−28% allocations vs. reflection).

- **🛡️ Built-In Cache Protection**
  - **Anti-Stampede**: Segmented in-memory locks + distributed locks coalesce concurrent reloads; lock timeouts gracefully degrade to lock-free direct reads (availability first).
  - **Anti-Penetration**: Optional null-value caching (`@@NULL@@` sentinel + dedicated TTL).
  - **Anti-Avalanche**: Background refresh + TTL ceiling fallback.

- **📊 Full-Link Observability**
  - OpenTelemetry-standard `ActivitySource` (Tracing) and `Meter` (Metrics).
  - Structured operation logs (Debug-level hit logs guarded by `IsEnabled`, zero overhead when disabled).

## 📚 Documentation

| Document | Description |
|------|------|
| [**Getting Started**](docs/Getting-Started.md) | Integrate L2Cache from scratch |
| [**Configuration Guide**](docs/Configuration-Guide.md) | Global and region configuration options in detail |
| [**API Reference**](docs/API-Reference.md) | `ICacheClient`, `ILoader`, and policy interfaces |
| [**Advanced Features**](docs/Advanced-Features.md) | Locking, null-value caching, background refresh, and invalidation sync internals |
| [**Telemetry**](docs/Telemetry.md) | OpenTelemetry metrics/tracing setup & semantics |
| [**Architecture**](docs/structure.md) | Internal architecture and module layout |

## 📦 Installation

```bash
dotnet add package L2Cache
```

Install extension packages as needed:

```bash
# Telemetry (OpenTelemetry Metrics/Tracing)
dotnet add package L2Cache.Telemetry

# Serialization extensions
dotnet add package L2Cache.Serializers.Json      # System.Text.Json (default)
dotnet add package L2Cache.Serializers.MemoryPack # High-performance binary
```

## 🚀 Quick Start

### 1. Register Services

```csharp
using L2Cache;
using L2Cache.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;   // Enable L1 memory cache
    options.UseRedis = true;        // Enable L2 Redis cache
    options.Redis.ConnectionString = builder.Configuration.GetConnectionString("Redis");
})
.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(30);
})
.WithLoader<ProductLoader>()       // Data loader (resolved from DI, supports Scoped dependencies)
.WithBackgroundRefresh();          // Optional: background refresh
```

### 2. Define a Loader

```csharp
public class ProductLoader : ILoader<int, ProductDto>
{
    private readonly IProductRepository _repo;

    public ProductLoader(IProductRepository repo) => _repo = repo;

    public async Task<ProductDto?> LoadAsync(int key, CancellationToken cancellationToken = default)
        => await _repo.GetByIdAsync(key, cancellationToken);

    // Batch loading: translate to a single IN query in real scenarios
    public async Task<Dictionary<int, ProductDto>> LoadManyAsync(
        IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
        => await _repo.GetByIdsAsync(keys, cancellationToken);
}
```

### 3. Inject and Use

```csharp
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ICacheClient<int, ProductDto> _cache;

    public ProductsController(ICacheClient<int, ProductDto> cache) => _cache = cache;

    [HttpGet("{id}")]
    public async Task<ProductDto?> Get(int id)
        => await _cache.GetOrLoadAsync(id);   // On miss, loads from source and backfills L1/L2

    [HttpPut("{id}")]
    public async Task Put(int id, ProductDto dto)
        => await _cache.PutAsync(id, dto);    // Writes L1 + L2 and broadcasts invalidation

    [HttpDelete("{id}")]
    public async Task Delete(int id)
        => await _cache.EvictAsync(id);       // Removes and broadcasts invalidation
}
```

## 🤝 Contributing

Issues and Pull Requests are welcome! Please run `dotnet test` and ensure all tests pass before submitting.

## 📄 License

This project is licensed under the [MIT License](LICENSE).
