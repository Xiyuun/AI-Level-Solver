@echo off
rem LevelForge one-click setup for a new device.
rem Real work lives in tools\setup.ps1 (auto-installs .NET 10 SDK + Node.js per-user, then builds).
rem ASCII only in .cmd files: cmd.exe parses batch files in the OEM codepage.
setlocal
cd /d "%~dp0"

echo.
echo   LevelForge one-click setup
echo   ========================
echo   This will check/install .NET 10 SDK and Node.js for the current user,
echo   then build the platform, the UI and the game plugins.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\setup.ps1" %*
set "CODE=%errorlevel%"

if not "%CODE%"=="0" (
  echo.
  echo Setup failed with exit code %CODE% - see the message above.
  echo Tips: run this file again after fixing the problem, or use the packaged
  echo       release ^(publish\ folder^) which needs nothing installed.
  echo.
  pause
)
exit /b %CODE%
