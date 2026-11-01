@echo off
rem LevelForge one-click start (Windows CMD: double-click or run from terminal)
rem Adds user-level toolchain PATH; builds web assets on first run; serves http://localhost:5000
cd /d %~dp0
set PATH=%USERPROFILE%\.dotnet;%USERPROFILE%\nodejs;%PATH%
set DOTNET_ROOT=%USERPROFILE%\.dotnet

if not exist web\dist (
  where node >nul 2>nul
  if %errorlevel%==0 (
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
dotnet run --project src\LevelForge.Platform --urls http://localhost:5000
pause
