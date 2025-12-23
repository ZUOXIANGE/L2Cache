# 启动本地开发环境（统一入口）
param(
  [switch]$Monitoring,
  [switch]$Benchmarks
)

Write-Host "🚀 启动 L2Cache 本地开发环境..." -ForegroundColor Green

# 检查 Docker
try { docker version | Out-Null } catch { Write-Host "❌ Docker 未运行" -ForegroundColor Red; exit 1 }

# 预检查网络冲突：如本机已存在同名网络且地址段冲突，可先提示处理
$networkName = "l2cache-network"
try {
  $networks = docker network ls --format '{{.Name}}'
  if ($networks -contains $networkName) {
    Write-Host "ℹ️ 检测到现有网络: $networkName" -ForegroundColor Yellow
    Write-Host "   如遇 'address pool overlap' 错误，可执行: docker network rm $networkName" -ForegroundColor Yellow
  }
} catch {}

# 组装 docker-compose 参数
$composeArgs = @('up','-d')
if ($Monitoring) { $composeArgs = @('--profile','monitoring') + $composeArgs }
if ($Benchmarks) { $composeArgs = @('--profile','benchmarks') + $composeArgs }

Write-Host "🔄 执行: docker-compose $($composeArgs -join ' ')" -ForegroundColor Cyan
docker-compose @composeArgs

Write-Host "📊 服务状态:" -ForegroundColor Cyan
docker-compose ps

Write-Host "🎉 开发环境启动完成！" -ForegroundColor Green
Write-Host "👉 Redis: localhost:6379 | 示例API: http://localhost:5000 | Redis Commander: http://localhost:8081" -ForegroundColor White
if ($Monitoring) {
    Write-Host "👉 OpenObserve: http://localhost:5080 (admin@example.com/admin123)" -ForegroundColor White
}