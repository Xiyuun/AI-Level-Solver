@echo off
rem LevelForge release packager: builds a self-contained Windows folder + zip.
rem Real work lives in tools\package.ps1. ASCII only in .cmd files.
setlocal
cd /d "%~dp0"

echo.
echo   LevelForge release packager
echo   ==========================
echo   Produces publish\ (self-contained, no prerequisites) and dist\*.zip
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\package.ps1" %*
set "CODE=%errorlevel%"

if not "%CODE%"=="0" (
  echo.
  echo Packaging failed with exit code %CODE% - see the message above.
  echo.
  pause
)
exit /b %CODE%
