@echo off
rem LevelForge launcher (source tree): builds are done by setup.cmd; this just starts the platform.
rem ASCII only in .cmd files (cmd.exe parses them in the OEM codepage).
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem Prefer the user-level .NET 10 SDK when present (same rule as run.cmd/build.cmd)
set "PATH=%USERPROFILE%\nodejs;%PATH%"
if exist "%USERPROFILE%\.dotnet\sdk\10.*" (
  set "PATH=%USERPROFILE%\.dotnet;!PATH!"
  set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
)

rem Already running? Just open the browser.
netstat -ano | findstr ":5000" | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 (
  echo LevelForge is already running: http://localhost:5000
  start "" "http://localhost:5000"
  timeout /t 2 >nul
  exit /b 0
)

if not exist "web\dist\index.html" (
  echo WARNING: web\dist is missing - the UI will not be available.
  echo          Run setup.cmd once to build the frontend, or use the packaged release.
)

echo Starting LevelForge on http://localhost:5000 ...
echo (keep this window open; press Ctrl+C to stop)
rem Open the browser a few seconds later, once Kestrel is listening.
rem (PowerShell does the delay+open: nested quotes in "start cmd /c" break easily)
start "" /min powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 6; Start-Process 'http://localhost:5000'"

dotnet run --project src\LevelForge.Platform --urls http://localhost:5000
pause
