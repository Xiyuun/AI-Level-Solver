@echo off
rem LevelForge 一键构建+测试（Windows CMD）
cd /d %~dp0

rem 用户级工具链（新设备装在 %USERPROFILE%\.dotnet 与 %USERPROFILE%\nodejs，不动系统安装）
set PATH=%USERPROFILE%\.dotnet;%USERPROFILE%\nodejs;%PATH%
set DOTNET_ROOT=%USERPROFILE%\.dotnet

dotnet build LevelForge.slnx -c Debug
if errorlevel 1 exit /b 1
dotnet test LevelForge.slnx -c Debug --no-build
if errorlevel 1 exit /b 1

rem 前端：node 可用时跑 vitest（依赖缺失先安装）
where node >nul 2>nul
if %errorlevel%==0 (
  pushd web
  if not exist node_modules call npm install --no-audit --no-fund
  call npm run test
  popd
)
