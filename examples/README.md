# L2Cache Examples

本项目展示了如何在 ASP.NET Core Web API 项目中集成和使用 L2Cache。

## 简介

该示例项目演示了 L2Cache 的核心功能和最佳实践，包括：
- 基础缓存操作 (Get/Set/Remove)
- 继承 `L2CacheService` 实现自定义业务缓存
- 继承 `AbstractCacheService` 实现完全自定义的缓存逻辑
- OpenTelemetry 监控与指标集成
- Scalar API 文档集成

## 前置条件

- **.NET SDK**: 建议使用 .NET 10 或更高版本。
- **Redis**: 项目默认连接到 `localhost:6379`。你需要准备一个运行中的 Redis 实例。

可以使用 Docker 快速启动 Redis：
```bash
docker run -d -p 6379:6379 --name l2cache-redis redis
```

## 运行项目

在 `examples/L2Cache.Examples` 目录下执行以下命令：

```bash
dotnet run
```

项目启动后，默认监听端口为 `5028` (HTTP) 和 `7116` (HTTPS)。

## 使用说明

### API 文档 (Scalar)

启动项目后，访问根路径即可查看交互式 API 文档：

- **URL**: [http://localhost:5028](http://localhost:5028)

你可以在 Scalar UI 中直接发送请求，测试各个接口的功能。

### 主要演示模块

1.  **基础用法 (BasicsController)**
    - 展示如何直接注入 `IL2Cache` 接口进行简单的键值对操作。
    - 路由: `/api/basics`

2.  **产品缓存 (ProductController)**
    - 展示如何通过继承 `L2CacheService<T>` 来创建特定于业务实体的缓存服务。
    - 演示了强类型对象的缓存处理。
    - 路由: `/api/product`

3.  **自定义缓存 (CustomInheritanceController)**
    - 展示如何继承 `AbstractCacheService` 以获得最大的灵活性。
    - 路由: `/api/custom`

4.  **监控与遥测**
    - 项目配置了 OpenTelemetry，可以导出 Metrics 和 Traces。
    - 可以在控制台日志中看到部分追踪信息，或配置 OTEL Endpoint 进行可视化。
