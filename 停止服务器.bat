@echo off
chcp 65001 >nul
title BallBattle-4 停止服务器

echo 正在停止 SpacetimeDB 服务器...
spacetime server stop 2>nul

:: 强制结束 spacetime 进程
taskkill /f /im "spacetimedb-cli.exe" >nul 2>&1

echo ✅ 服务器已停止。
timeout /t 2 /nobreak >nul
