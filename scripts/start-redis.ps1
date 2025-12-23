# L2Cache Redis 启动脚本
# 用于快速启动开发环境所需的Redis服务

Write-Host "🚀 启动 L2Cache Redis 开发环境..." -ForegroundColor Green

# 检查Docker是否运行
try {
    docker version | Out-Null
    Write-Host "✅ Docker 服务正在运行" -ForegroundColor Green
} catch {
    Write-Host "❌ Docker 服务未运行，请先启动 Docker Desktop" -ForegroundColor Red
    exit 1
}

# 检查docker-compose是否可用
try {
    docker-compose version | Out-Null
    Write-Host "✅ Docker Compose 可用" -ForegroundColor Green
} catch {
    Write-Host "❌ Docker Compose 不可用，请确保已安装 Docker Compose" -ForegroundColor Red
    exit 1
}

# 启动服务
Write-Host "🔄 启动 Redis 和 Redis Commander..." -ForegroundColor Yellow
docker-compose up -d

# 等待服务启动
Write-Host "⏳ 等待服务启动..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# 检查服务状态
Write-Host "📊 检查服务状态..." -ForegroundColor Yellow
docker-compose ps

# 测试Redis连接
Write-Host "🔍 测试 Redis 连接..." -ForegroundColor Yellow
try {
    $result = docker exec l2cache-redis redis-cli ping
    if ($result -eq "PONG") {
        Write-Host "✅ Redis 连接成功！" -ForegroundColor Green
    } else {
        Write-Host "❌ Redis 连接失败" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ 无法连接到 Redis" -ForegroundColor Red
}

Write-Host ""
Write-Host "🎉 Redis 开发环境启动完成！" -ForegroundColor Green
Write-Host ""
Write-Host "📋 服务信息:" -ForegroundColor Cyan
Write-Host "  Redis 服务器: localhost:6379" -ForegroundColor White
Write-Host "  Redis Commander (Web UI): http://localhost:8081" -ForegroundColor White
Write-Host "    用户名: admin" -ForegroundColor Gray
Write-Host "    密码: admin123" -ForegroundColor Gray
Write-Host ""
Write-Host "🛠️  常用命令:" -ForegroundColor Cyan
Write-Host "  查看日志: docker-compose logs -f redis" -ForegroundColor White
Write-Host "  停止服务: docker-compose down" -ForegroundColor White
Write-Host "  重启服务: docker-compose restart" -ForegroundColor White
Write-Host "  连接Redis: docker exec -it l2cache-redis redis-cli" -ForegroundColor White
Write-Host ""