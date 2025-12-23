# 项目目录结构说明

本文档描述 L2Cache 项目的标准化目录结构及各目录职责，帮助贡献者快速定位代码与资源。

## 核心目录职责（规范）

- `src/`：生产代码（仅包含库或组件），每个子项目自带 `*.csproj` 与源代码。
  - `L2Cache/`：核心库，包含缓存服务实现与基础逻辑。
  - `L2Cache.Abstractions/`：抽象层，定义接口、模型与扩展点。
  - `L2Cache.Serializers.*/`：序列化适配器（如 Json, MemoryPack）。
  - `L2Cache.Telemetry/`：遥测、日志与健康检查扩展。
- `tests/`：测试工程。
  - `L2Cache.Tests.Functional/`：包含单元测试、集成测试与端到端功能验证。
- `examples/`：示例与集成演示工程，不作为生产依赖；用于文档与教学。
- `benchmarks/`：基于 BenchmarkDotNet 的微基准测试工程 (`L2Cache.Benchmarks`)。
- `docs/`：用户指南、API 参考、示例与结构说明。
- `docker/`：容器与监控相关文件（`docker-compose.yml`, `redis.conf`, Dockerfiles 等）。
- `.github/`：CI/CD 工作流、模板与发布规则。
- `L2Cache.slnx`：项目解决方案文件。
- `Directory.Build.props` / `Directory.Packages.props`：全局构建配置与包版本管理。

## 目录职责与约定

- `src/`: 仅放置生产环境代码，每个子项目自包含 `*.csproj`、源代码与本项目特定的 README（如需要）。
- `tests/`: 
  - `L2Cache.Tests.Functional`: 核心功能验证，集成 Testcontainers 进行真实环境测试。
- `examples/`: 示例应用与集成案例，避免作为生产依赖被引用；用于文档与演示。
- `benchmarks/`: 细粒度的代码性能基准测试。
- `docs/`: 用户指南、API 文档与操作手册；新增模块请补充对应指南与示例。
- `docker/`: 包含开发环境所需的容器配置（如 Redis）。

## 常用路径（约定）

- Redis 配置：`docker/redis.conf`（由 `docker/docker-compose.yml` 挂载引用）。
- 本地开发环境：`docker/docker-compose.yml`（用于启动 Redis 等依赖）。
- 示例工程：`examples/L2Cache.Examples`。

## 维护与命名（规范）

- 子项目命名：`L2Cache.{Module}`，示例工程命名：`L2Cache.Examples.{Scenario}`。
- 测试项目命名：`L2Cache.Tests.{Type}`（如 `Functional`）。
- 重要元文件：每个项目包含 `*.csproj`，示例含 `appsettings.json`。
- 解决方案：使用 `L2Cache.slnx`。
- 依赖管理：通过 `Directory.Packages.props` 集中管理 NuGet 包版本。
