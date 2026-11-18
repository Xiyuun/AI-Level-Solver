@echo off
rem One-command setup for a new device: build solution + frontend + full tests,
rem then build and deploy all game plugin DLLs into platform data/plugins/.
setlocal
cd /d %~dp0

echo [1/3] build solution + frontend + full tests (build.cmd)...
call build.cmd
if errorlevel 1 ( echo BUILD/TEST FAILED - fix before deploy & exit /b 1 )

echo [2/3] build and deploy game plugin DLLs...
call deploy-plugins.cmd
if errorlevel 1 ( echo PLUGIN DEPLOY HAD FAILURES - see messages above & exit /b 1 )

echo [3/3] done. Startup checklist:
echo   1. run.cmd to start the platform (http://localhost:5000)
echo   2. Settings page: fill LLM base URL / API key / model (needed for level generation)
echo   3. If the game projects exist on this device, point each levels root at the
echo      project's level data directory and re-import the corpus (wizard step 1)
echo   4. Gate-2 real-game pairing needs the game projects themselves
endlocal
