# L2Cache 快速入门指南

本指南将帮助您从零开始集成 L2Cache 到您的 .NET 应用程序中。

## 📋 前置条件

- .NET 8.0 或更高版本
- Redis 服务器 (仅当使用二级缓存时需要)
  - 推荐使用 Docker 启动: `docker run -d -p 6379:6379 redis`

## 1. 安装 NuGet 包

### 核心包 (必须)

```bash
dotnet add package L2Cache
```

### 扩展包 (可选)

如果需要遥测与健康检查功能 (Metrics, Tracing, HealthCheck)，请安装 Telemetry 扩展：

```bash
dotnet add package L2Cache.Telemetry
```

如果需要更高性能的二进制序列化，可以安装 MemoryPack 适配器：

```bash
dotnet add package L2Cache.Serializers.MemoryPack
```

## 2. 基础配置

在 `Program.cs` 中注册 L2Cache 服务。

```csharp
using L2Cache.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 注册 L2Cache
builder.Services.AddL2Cache(options =>
{
    // --- L1: 本地内存缓存 ---
    // 启用本地内存缓存，适用于高频访问的热点数据
    options.UseLocalCache = true;
    
    // --- L2: Redis 分布式缓存 ---
    // 启用 Redis 缓存，适用于分布式共享和数据持久化
    options.UseRedis = true;
    options.Redis.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

    // --- 监控与遥测 (需要 L2Cache.Telemetry 包) ---
    options.Telemetry.EnableMetrics = true;
    options.Telemetry.EnableTracing = true;
    options.Telemetry.EnableHealthCheck = true;

    // --- 后台刷新 (防止雪崩) ---
    // 启用后台刷新机制，自动更新即将过期的缓存
    options.BackgroundRefresh.Enabled = true;

    // --- 并发锁 (高级功能) ---
    // 启用锁机制防止缓存击穿和并发冲突
    options.Lock.EnabledMemoryLock = true;
    options.Lock.EnabledDistributedLock = true;
})
.AddL2CacheTelemetry(); // 注入遥测服务
```

## 3. 使用模式

L2Cache 提供了两种主要的使用方式，适应不同复杂度的业务场景。

### 模式 A: 继承 `L2CacheService` (推荐)

这是**最佳实践**。通过继承 `L2CacheService<TKey, TValue>`，您可以集中管理特定业务实体的缓存逻辑，并自动获得 "Cache Aside"（缓存缺失回源）的能力。

**定义服务:**

```csharp
public class ProductCacheService : L2CacheService<int, ProductDto>
{
    private readonly IProductRepository _repository;

    public ProductCacheService(
        IServiceProvider sp,
        IOptions<L2CacheOptions> opts,
        ILogger<L2CacheService<int, ProductDto>> logger,
        IProductRepository repository) 
        : base(sp, opts, logger)
    {
        _repository = repository;
    }

    // 1. 定义缓存名称 (Redis Key 前缀)
    public override string GetCacheName() => "products";

    // 2. 定义 Key 生成规则
    public override string BuildCacheKey(int id) => id.ToString();

    // 3. 定义回源逻辑 (当 L1 和 L2 都未命中时调用)
    public override async Task<ProductDto?> QueryDataAsync(int id)
    {
        // 模拟数据库查询
        return await _repository.GetByIdAsync(id);
    }
}
```

**使用服务:**

```csharp
[ApiController]
[Route("api/products")]
public class ProductController : ControllerBase
{
    private readonly ProductCacheService _productCache;

    public ProductController(ProductCacheService productCache)
    {
        _productCache = productCache;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        // GetOrLoadAsync 会自动处理:
        // 1. 查 L1 -> 命中返回
        // 2. 查 L2 -> 命中返回并回填 L1
        // 3. 查 DB (QueryDataAsync) -> 命中返回并回填 L1, L2
        var product = await _productCache.GetOrLoadAsync(id);
        
        return product != null ? Ok(product) : NotFound();
    }
}
```

### 模式 B: 直接使用 `ICacheService` (简单场景)

适用于验证码、临时标记、简单配置等不需要自动回源的数据，或者你希望手动控制缓存逻辑。

**使用方法:**

直接注入 `ICacheService<string, string>` (或其它简单类型)。

```csharp
[ApiController]
[Route("api/cache")]
public class CacheController : ControllerBase
{
    // 注入通用缓存服务
    private readonly ICacheService<string, string> _cache;

    public CacheController(ICacheService<string, string> cache)
    {
        _cache = cache;
    }

    [HttpPost]
    public async Task<IActionResult> Set(string key, string value)
    {
        // 手动写入缓存 (同时写入 L1 和 L2)
        await _cache.PutAsync(key, value, TimeSpan.FromMinutes(30));
        return Ok();
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        // 读取缓存 (优先 L1，其次 L2)
        var value = await _cache.GetAsync(key);
        return value != null ? Ok(value) : NotFound();
    }
}
```

## 4. 下一步

- 查看 [配置指南](Configuration-Guide.md) 了解更多高级配置。
- 查看 [API 参考](API-Reference.md) 了解详细接口说明。
- 运行 `examples/` 目录下的示例项目体验实际效果。
