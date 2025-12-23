# 项目目录结构说明

本文档描述 L2Cache 项目的标准化目录结构及各目录职责，帮助贡献者快速定位代码与资源。

## 顶层结构（精简）

```
/
├── src/
├── tests/
├── examples/
├── benchmarks/
├── docs/
├── docker/
├── scripts/
├── .github/
├── README.md
├── CHANGELOG.md
├── CONTRIBUTING.md
└── docker-compose.yml
```

## 核心目录职责（规范）

- `src/`：生产代码（仅包含库或组件），每个子项目自带 `*.csproj` 与源代码。
- `tests/`：测试工程，分为功能测试与性能测试两个主要项目。
  - `L2Cache.Tests.Functional/`：包含单元测试、集成测试与端到端功能验证。
  - `L2Cache.Tests.Performance/`：包含性能基准测试与负载测试。
- `examples/`：示例与集成演示工程，不作为生产依赖；用于文档与教学。
- `benchmarks/`：基于 BenchmarkDotNet 的微基准测试工程。
- `docs/`：用户指南、API 参考、示例与结构说明。
- `docker/`：容器与监控相关文件（Dockerfiles、`redis.conf` 等）。
- `scripts/`：开发辅助脚本（统一入口 `dev-up.ps1`/`dev-down.ps1`，轻量 `start-redis.ps1`/`stop-redis.ps1`）。
- `.github/`：CI/CD 工作流、模板与发布规则。
- `docker-compose.yml`：顶层 Compose，用于本地一键启动依赖环境。

## 目录职责与约定

- `src/`: 仅放置生产环境代码，每个子项目自包含 `*.csproj`、源代码与本项目特定的 README（如需要）。
- `tests/`: 
  - `L2Cache.Tests.Functional`: 核心功能验证，集成 Testcontainers 进行真实环境测试。
  - `L2Cache.Tests.Performance`: 性能指标验证，包含高并发场景下的压力测试。
- `examples/`: 示例应用与集成案例，避免作为生产依赖被引用；用于文档与演示。
- `benchmarks/`: 细粒度的代码性能基准测试。
- `docs/`: 用户指南、API 文档与操作手册；新增模块请补充对应指南与示例。
- `docker/`: 与容器相关的文件（各 Dockerfile、Redis 配置等）。
- `scripts/`: 常用开发脚本，如启动/停止 Redis 与监控、初始化环境等。

## 常用路径（约定）

- 开发脚本：`scripts/dev-up.ps1`、`scripts/dev-down.ps1`（统一入口）；`scripts/start-redis.ps1`、`scripts/stop-redis.ps1`（轻量模式）。
- Redis 配置：`docker/redis.conf`（由 `docker-compose.yml` 挂载引用）。
- Advanced Examples: `examples/L2Cache.Examples` (see `docs/examples/web-api.md`).

## 维护与命名（规范）

- 子项目命名：`L2Cache.{Module}`，示例工程命名：`L2Cache.Examples.{Scenario}`。
- 测试项目命名：`L2Cache.Tests.{Type}`（如 `Functional`, `Performance`）。
- 重要元文件：每个项目包含 `*.csproj`，示例含 `appsettings.json`/`appsettings.Development.json`（如需要）。
- 脚本与容器：脚本集中在 `scripts/`；容器与监控配置集中在 `docker/`，避免分散。
