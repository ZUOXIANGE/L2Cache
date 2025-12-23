# L2Cache 示例项目

本项目包含全面的示例代码，演示如何在 .NET 应用程序中使用 L2Cache 的各项功能。

## ✨ 演示功能

### 1. 基础操作 (`BasicsController`)
- **简单缓存**: 演示 Get, Set, Remove, Exists 等基本键值对操作。
- **清空**: 清空所有缓存项。
- **TTL**: 为缓存项设置过期时间。

### 2. 实体缓存 (`ProductController`)
- **Cache Aside 模式**: 演示缓存未命中时自动从数据源（模拟数据库）加载数据。
- **CRUD 操作**: 创建、读取、更新、删除，并自动处理缓存的失效与更新。
- **批量操作**: 高效获取多个条目。
- **强制刷新**: 强制从数据源重新加载数据。
- **清除**: 清除所有产品缓存。

### 3. 高级特性 (`AdvancedController`)
- **序列化切换**:
  - `POST /api/advanced/serializer/json`: 切换为 System.Text.Json (默认)
  - `POST /api/advanced/serializer/memorypack`: 切换为 MemoryPack (高性能二进制)
- **健康与统计**:
  - `GET /api/advanced/stats`: 检查 Redis 连接状态。

### 4. 遥测与健康检查 (`TelemetryController`)
- **健康检查**: `GET /api/telemetry/health` (需要 `L2Cache.Telemetry` 包)
- **指标**: `GET /api/telemetry/metrics` - 查看内部缓存统计信息。

### 5. 后台刷新
- 在 `Program.cs` 中启用，演示如何自动刷新即将过期的缓存项，防止缓存雪崩。

## 🚀 如何运行

1. 确保安装了 .NET 8.0 或更高版本的 SDK。
2. (可选) 启动 Redis: `docker run -d -p 6379:6379 redis`。如果 Redis 不可用，应用将自动降级为本地内存缓存或模拟模式。
3. 运行项目:
   ```bash
   cd L2Cache.Examples
   dotnet run
   ```
4. 访问 Swagger UI: `http://localhost:5000/swagger`

## 🧪 测试

集成测试位于 `tests/L2Cache.Tests.Functional/Examples`。
运行测试:
```bash
dotnet test tests/L2Cache.Tests.Functional
```
