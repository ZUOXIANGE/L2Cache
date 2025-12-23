# L2Cache Redis 停止脚本
# 用于停止开发环境的Redis服务

Write-Host "🛑 停止 L2Cache Redis 开发环境..." -ForegroundColor Yellow

# 检查Docker是否运行
try {
    docker version | Out-Null
    Write-Host "✅ Docker 服务正在运行" -ForegroundColor Green
} catch {
    Write-Host "❌ Docker 服务未运行" -ForegroundColor Red
    exit 1
}

# 显示当前运行的服务
Write-Host "📊 当前运行的服务:" -ForegroundColor Cyan
docker-compose ps

# 停止服务
Write-Host "🔄 停止所有服务..." -ForegroundColor Yellow
docker-compose down

# 可选：清理数据卷（取消注释下面的行来清理数据）
# Write-Host "🗑️  清理数据卷..." -ForegroundColor Yellow
# docker-compose down -v

Write-Host ""
Write-Host "✅ Redis 开发环境已停止！" -ForegroundColor Green
Write-Host ""
Write-Host "💡 提示:" -ForegroundColor Cyan
Write-Host "  如需完全清理（包括数据）: docker-compose down -v" -ForegroundColor White
Write-Host "  如需重新启动: .\scripts\start-redis.ps1" -ForegroundColor White
Write-Host ""