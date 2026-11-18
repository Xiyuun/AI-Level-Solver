@echo off
rem Build and deploy all game plugin DLLs into platform data/plugins/
rem NOTE: if the platform is running, plugin DLLs are locked - stop the platform first, then re-run this script.
setlocal
cd /d %~dp0

set PLUGINS=src\LevelForge.Platform\data\plugins

echo [piggyfarm] build Release...
dotnet build adapters\piggyfarm\LevelForge.Adapters.PiggyFarm\LevelForge.Adapters.PiggyFarm.csproj -c Release --nologo -v q
if errorlevel 1 ( echo [piggyfarm] BUILD FAILED & goto :fail )
if not exist "%PLUGINS%\piggyfarm" mkdir "%PLUGINS%\piggyfarm"
copy /y "adapters\piggyfarm\LevelForge.Adapters.PiggyFarm\bin\Release\net10.0\LevelForge.Adapters.PiggyFarm.dll" "%PLUGINS%\piggyfarm\" >nul
if errorlevel 1 (
  echo [piggyfarm] COPY FAILED: DLL locked by the running platform - stop the platform and re-run this script
  goto :fail
)
echo [piggyfarm] deployed

echo All plugins deployed. Start the platform (run.cmd) to load them - gate1 self-check runs on detect.
exit /b 0

:fail
echo Plugin deployment had failures: stop the platform and re-run deploy-plugins.cmd
exit /b 1
