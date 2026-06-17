@echo off
chcp 65001 >nul
title BallBattle-4 SpacetimeDB 服务器

echo ========================================
echo   BallBattle-4 SpacetimeDB 服务器
echo ========================================
echo.

:: 检查 spacetimedb CLI 是否存在
where spacetime >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未找到 spacetimedb CLI 工具！
    echo 请先安装 SpacetimeDB CLI：
    echo   PowerShell: iwr https://spacetimedb.com/install -useb ^| iex
    echo.
    pause
    exit /b 1
)

echo [1/5] 添加防火墙规则 (端口 3000)...
netsh advfirewall firewall add rule name="SpacetimeDB-3000" dir=in action=allow protocol=TCP localport=3000 >nul 2>&1
if %errorlevel% equ 0 (echo   防火墙规则已添加) else (echo   规则已存在或添加失败（可忽略）)

echo [2/5] 停止已有的服务器实例...
spacetime server stop 2>nul

echo [3/5] 启动 SpacetimeDB 独立服务器 (监听 0.0.0.0:3000)...
:: --in-memory 表示数据仅存在内存中，重启后清空
:: 去掉 --in-memory 则数据持久化到磁盘
start "SpacetimeDB-Server" spacetime start -l 0.0.0.0:3000 --in-memory

:: 等待服务器启动
echo [4/5] 等待服务器就绪...
timeout /t 3 /nobreak >nul

:: 检查服务器是否成功启动
spacetime server ping -s http://127.0.0.1:3000 >nul 2>&1
if %errorlevel% neq 0 (
    echo [警告] 无法ping通服务器，尝试直接发布...
)

echo [5/5] 发布游戏模块...
cd /d "%~dp0..\server-csharp\spacetimedb"
spacetime publish -s http://127.0.0.1:3000 ballbattle4

echo.
echo ========================================
echo   ✅ 服务器启动完成！
echo.
echo   本机加入: 打开游戏 → 选择「本机模式」→ 连接
echo   局域网玩家加入: 打开游戏 → 选择「局域网模式」
echo                   → 填入你的IP → 连接
echo.
echo   你的局域网IP可能是:
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do echo     %%a
echo.
echo   按任意键关闭此窗口（不会停止服务器）
echo ========================================
pause
