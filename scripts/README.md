# Scripts

这些脚本为本仓库开发与演示环境提供统一入口。

## 可用脚本

- `dev-up.ps1`：启动完整开发环境（Redis、Redis Commander、示例 API 等）。
  - 支持可选参数：`-Monitoring` 启动 OpenObserve，`-Benchmarks` 启动基准测试容器。
  - 示例：`./scripts/dev-up.ps1 -Monitoring`。

- `dev-down.ps1`：停止开发环境，等价于 `docker-compose down`。

- `start-redis.ps1`：仅启动 Redis 与 Redis Commander（轻量模式）。

- `stop-redis.ps1`：停止 Redis 与 Redis Commander。

## 注意事项

- 如遇 `address pool overlap` 网络冲突错误，可执行 `docker network rm l2cache-network` 后重试。
- 开发环境默认端口：
  - Redis: `6379`
  - Redis Commander: `8081`
  - 示例 API（容器内）: `5000`