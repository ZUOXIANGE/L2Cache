# L2Cache Performance Benchmarks

本项目包含 L2Cache 的性能基准测试，使用 [BenchmarkDotNet](https://benchmarkdotnet.org/) 构建。

## 简介

该项目旨在评估 L2Cache 在不同场景下的性能表现，包括：
- 基础读写操作 (Get/Set)
- 混合负载场景
- 序列化/反序列化开销
- L1 (内存) 与 L2 (Redis) 缓存的协同工作性能

## 前置条件

- **.NET SDK**: 建议使用 .NET 10 或更高版本。
- **Docker**: 基准测试依赖 Redis，项目会自动使用 FluentDocker 启动 Redis 容器，请确保 Docker Desktop 已启动并运行。

## 运行基准测试

在 `benchmarks/L2Cache.Benchmarks` 目录下执行以下命令：

```bash
dotnet run -c Release
```

注意：**必须**使用 `Release` 配置运行，以获取准确的性能数据。

### 过滤运行

可以使用过滤器只运行特定的测试：

```bash
# 运行所有包含 "Basic" 的测试
dotnet run -c Release --filter *Basic*

# 运行所有包含 "Advanced" 的测试
dotnet run -c Release --filter *Advanced*
```

## 项目结构

- **BasicBenchmarks.cs**: 包含最基础的缓存操作测试，如 `Get`、`Set`、`Remove`。
- **AdvancedBenchmarks.cs**: 包含更复杂的场景，如并发访问、大数据量读写等。
- **RedisContainer.cs**: 负责管理基准测试所需的 Redis 容器资源。
