# L2Cache Examples

本项目展示了如何在 ASP.NET Core Web API 项目中集成和使用 L2Cache（`ICacheClient` 门面 + `ILoader` 回源）。

## 简介

该示例项目演示了 L2Cache 的核心功能和最佳实践，包括：
- 直接注入 `ICacheClient<TKey, TValue>` 进行基础键值操作（无需任何基类）
- 通过 `ILoader<TKey, TValue>` 回源加载器对接数据源（支持 Scoped 依赖）
- 区域化配置（每个缓存区域独立的 TTL / 锁 / 空值策略）
- 后台刷新（`WithBackgroundRefresh`）
- 替换序列化器（MemoryPack）与 OpenTelemetry 遥测集成
- Scalar API 文档集成

## 前置条件

- **.NET SDK**: 建议使用 .NET 10 或更高版本。
- **Redis**: 项目默认连接到 `localhost:6379`。你需要准备一个运行中的 Redis 实例。

可以使用 Docker 快速启动 Redis：
```bash
docker run -d -p 6379:6379 --name l2cache-redis redis
```

## 运行项目

在仓库根目录执行：

```bash
dotnet run --project examples/L2Cache.Examples
```

项目启动后默认监听 `http://localhost:5000`。

## 使用说明

### API 文档 (Scalar)

启动项目后，访问根路径即可查看交互式 API 文档：

- **URL**: [http://localhost:5000/scalar/v1](http://localhost:5000/scalar/v1)

你可以在 Scalar UI 中直接发送请求，测试各个接口的功能。

### 主要演示模块

1. **基础用法 ([BasicsController](L2Cache.Examples/Controllers/BasicsController.cs))**
   - 直接注入 `ICacheClient<string, string>`，展示 `GetAsync` / `PutAsync` / `EvictAsync` / `ExistsAsync`。
   - 路由: `/api/basics`

2. **产品缓存 ([ProductController](L2Cache.Examples/Controllers/ProductController.cs))**
   - 演示 `ILoader<int, ProductDto>` 回源加载器 + `WithBackgroundRefresh` 后台刷新。
   - 路由: `/api/product`

3. **自定义回源 ([CustomInheritanceController](L2Cache.Examples/Controllers/CustomInheritanceController.cs))**
   - 演示 "users" 区域使用 `CustomUserLoader`（`LoaderBase`，只实现单条查询，批量逐 Key 回源）。
   - 路由: `/api/custom-inheritance`

4. **批量与高级场景 ([AdvancedController](L2Cache.Examples/Controllers/AdvancedController.cs))**
   - 展示 L2（Redis）连接状态等运行观测。
   - 路由: `/api/advanced`

### 核心注册代码

完整配置见 [Program.cs](L2Cache.Examples/Program.cs)，核心片段：

```csharp
var l2Cache = builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;   // L1 内存缓存
    options.UseRedis = true;        // L2 Redis 缓存
    options.Redis.ConnectionString = "localhost:6379";
});

// 基础区域：无需 Loader
l2Cache.AddCache<string, string>("basics");

// 业务区域：Loader 回源 + 后台刷新
l2Cache.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(10);
})
    .WithLoader<ProductLoader>()
    .WithBackgroundRefresh(refresh => refresh.Interval = TimeSpan.FromMinutes(1));

// 可选：启用遥测（将 NoOp 替换为默认提供程序，输出指标/追踪）
builder.Services.AddL2CacheTelemetry();
```

## 监控与遥测

- 项目配置了 OpenTelemetry，可导出 Metrics（Meter: `L2Cache`）和 Traces（ActivitySource: `L2Cache`）。
- 通过环境变量 `OTEL_EXPORTER_OTLP_ENDPOINT` 指定 OTLP 端点进行可视化（默认 `http://localhost:5081`）。
