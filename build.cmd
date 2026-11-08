@echo off
rem LevelForge one-click build + test (Windows CMD)
rem ASCII only in this file: cmd.exe parses batch files in the OEM codepage, so UTF-8 text turns
rem into mojibake and can even be executed as commands. Keep every .cmd file pure ASCII + CRLF.
cd /d %~dp0

rem Prefer user-level dotnet ONLY when it has a 10.x SDK (an old user-level 8.x must not shadow system SDK)
set PATH=%USERPROFILE%\nodejs;%PATH%
if exist "%USERPROFILE%\.dotnet\sdk\10.*" (
  set PATH=%USERPROFILE%\.dotnet;%PATH%
  set DOTNET_ROOT=%USERPROFILE%\.dotnet
)

dotnet build LevelForge.slnx -c Debug
if errorlevel 1 exit /b 1
dotnet test LevelForge.slnx -c Debug --no-build
if errorlevel 1 exit /b 1

rem Frontend: run vitest when node is available (install deps first if missing)
where node >nul 2>nul
if %errorlevel%==0 (
  pushd web
  if not exist node_modules call npm install --no-audit --no-fund
  call npm run test
  popd
)
