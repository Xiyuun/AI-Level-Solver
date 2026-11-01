@echo off
rem LevelForge 一键启动（Windows CMD：双击或命令行运行）
rem 自动补用户级工具链 PATH；首次运行自动构建前端产物；随后启动平台并监听 http://localhost:5000
cd /d %~dp0
set PATH=%USERPROFILE%\.dotnet;%USERPROFILE%\nodejs;%PATH%
set DOTNET_ROOT=%USERPROFILE%\.dotnet

if not exist web\dist (
  where node >nul 2>nul
  if %errorlevel%==0 (
    echo [run] 首次运行：构建前端产物（约半分钟）...
    pushd web
    if not exist node_modules call npm install --no-audit --no-fund
    call npm run build
    popd
  ) else (
    echo [run] 提示：未安装 node，跳过前端构建；平台仍会启动但浏览器无页面（仅 API 可用）。
  )
)

echo [run] 启动平台：http://localhost:5000  （停止请按 Ctrl+C）
dotnet run --project src\LevelForge.Platform --urls http://localhost:5000
pause
