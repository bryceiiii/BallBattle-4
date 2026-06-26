# 停止 BallBattle-4 服务器
Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force
spacetime server stop 2>$null
Write-Host "✅ 所有服务已停止" -ForegroundColor Green
