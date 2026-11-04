@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem LevelForge one-click start (Windows CMD: double-click or run from terminal)
rem Adds user-level toolchain PATH; builds web assets on first run; serves http://localhost:5000
cd /d "%~dp0"
rem Prefer user-level dotnet ONLY when it has a 10.x SDK (an old user-level 8.x must not shadow system SDK)
rem NOTE: PATH inside a parenthesized block must use delayed expansion (!PATH!): %PATH% expands at
rem parse time, and unquoted entries containing spaces/parens (e.g. NVIDIA driver paths) break the
rem block parser -> the whole script dies instantly ("\NVIDIA was unexpected at this time.").
set "PATH=%USERPROFILE%\nodejs;%PATH%"
if exist "%USERPROFILE%\.dotnet\sdk\10.*" (
  set "PATH=%USERPROFILE%\.dotnet;!PATH!"
  set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
)

if not exist "web\dist" (
  where node >nul 2>nul
  if !errorlevel!==0 (
    echo [run] first run: building web assets, please wait...
    pushd web
    if not exist node_modules call npm install --no-audit --no-fund
    call npm run build
    popd
  ) else (
    echo [run] node not found, skip web build; platform will start without UI ^(API only^).
  )
)

echo [run] starting platform at http://localhost:5000  ^(press Ctrl+C to stop^)
rem 端口占用预检：已有一个实例在跑时，重新构建会被它锁定的 DLL 卡死（MSB3026/3027 刷屏）——
rem 先给出明确提示再停，而不是让用户面对半屏构建报错
netstat -ano | findstr ":5000" | findstr "LISTENING" >nul 2>nul
if !errorlevel!==0 (
  echo [run] 端口 5000 已被占用：很可能已有一个 LevelForge 实例在运行。
  echo [run] 新实例构建会被旧实例锁定的 DLL 卡死。请先关闭旧实例（关掉它的窗口，
  echo [run] 或 taskkill /F /IM LevelForge.Platform.exe）后再运行本脚本。
  pause
  exit /b 1
)
dotnet run --project src\LevelForge.Platform --urls http://localhost:5000
pause
