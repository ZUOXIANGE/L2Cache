# L2Cache Docker部署指南

## 快速开始

使用 `docker-compose` 命令启动环境：

### 基础服务启动
```bash
# 启动Redis和基本服务
docker-compose up -d

# 查看服务状态
docker-compose ps

# 查看日志
docker-compose logs -f
```

### 完整环境启动（含监控）
```bash
# 启动所有服务，包括OpenObserve
docker-compose --profile monitoring up -d
```

## 服务访问

| 服务 | 地址 | 说明 |
|------|------|------|
| L2Cache高级示例 | http://localhost:5000 | Swagger UI |
| Redis Commander | http://localhost:8081 | Redis管理界面 |
| OpenObserve | http://localhost:5080 | 可观测性平台 (admin@example.com/admin123) |

## Dockerfile 路径规范

为保持结构简洁，示例应用的 Dockerfile 与项目同目录：

- 示例应用：`examples/L2Cache.Examples/Dockerfile`

顶层 `docker-compose.yml` 已统一引用上述路径，避免重复文件。

## 环境配置

### 环境变量

#### L2Cache高级示例
- `ASPNETCORE_ENVIRONMENT`: 运行环境 (Development/Production)
- `ASPNETCORE_URLS`: 服务绑定地址
- `ConnectionStrings__Redis`: Redis连接字符串
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OpenTelemetry OTLP 端点
- `OTEL_EXPORTER_OTLP_HEADERS`: OpenTelemetry OTLP 请求头

#### Redis Commander
- `REDIS_HOSTS`: Redis主机配置
- `HTTP_USER`: 登录用户名
- `HTTP_PASSWORD`: 登录密码

#### OpenObserve
- `ZO_ROOT_USER_EMAIL`: 管理员邮箱
- `ZO_ROOT_USER_PASSWORD`: 管理员密码

### 数据卷

- `redis_data`: Redis数据持久化
- `openobserve_data`: OpenObserve数据持久化

## 监控配置

### OpenObserve

L2Cache 通过 OpenTelemetry (OTLP) 将以下数据推送到 OpenObserve：
- **Logs**: 应用日志
- **Metrics**: 性能指标 (Hits, Misses, Latency 等)
- **Traces**: 分布式调用链路

提示：首次启用监控（`--profile monitoring` 或 `-Monitoring`）后，访问 `http://localhost:5080` 使用 `admin@example.com/admin123` 登录。

## 性能调优

### Redis配置

编辑 `docker/redis.conf` 文件进行性能调优：
```conf
# 内存限制
maxmemory 256mb
maxmemory-policy allkeys-lru

# 持久化配置
save 900 1
save 300 10
save 60 10000

# 网络配置
tcp-backlog 511
timeout 0
tcp-keepalive 300
```

### Docker资源限制

在 `docker-compose.yml` 中添加资源限制：
```yaml
deploy:
  resources:
    limits:
      cpus: '0.5'
      memory: 512M
    reservations:
      cpus: '0.25'
      memory: 256M
```

## 故障排除

### 常见问题

1. **端口冲突**
   ```bash
   # 检查端口占用
   netstat -an | findstr "6379"
   
   # 修改docker-compose.yml中的端口映射
   ```

2. **容器启动失败**
   ```bash
   # 查看详细日志
   docker-compose logs [service-name]
   
   # 重新构建镜像
   docker-compose build --no-cache [service-name]
   ```

3. **Redis连接问题**
   ```bash
   # 测试Redis连接
   docker exec -it l2cache-redis redis-cli ping
   
   # 检查网络连接
   docker network ls
   docker network inspect l2cache_l2cache-network
   ```

### 性能监控

```bash
# 监控容器资源使用
docker stats

# 查看容器日志
docker logs -f --tail 100 [container-name]

# 进入容器调试
docker exec -it [container-name] /bin/sh
```
