# 停止本地开发环境（统一入口）
Write-Host "🛑 停止 L2Cache 本地开发环境..." -ForegroundColor Yellow

try { docker version | Out-Null } catch { Write-Host "❌ Docker 未运行" -ForegroundColor Red; exit 1 }

Write-Host "📊 当前服务状态:" -ForegroundColor Cyan
docker-compose ps

Write-Host "🔄 执行: docker-compose down" -ForegroundColor Cyan
docker-compose down

Write-Host "✅ 已停止。可使用 .\scripts\dev-up.ps1 重新启动。" -ForegroundColor Green