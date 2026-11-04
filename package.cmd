@echo off
rem LevelForge one-click package script (Windows CMD)
cd /d %~dp0

echo [package] 1/4 Building frontend...
pushd web
call npm run build
popd

echo [package] 2/4 .NET self-contained publish (win-x64)...
if exist publish rmdir /s /q publish
dotnet publish src\LevelForge.Platform -c Release -r win-x64 --self-contained -o publish

echo [package] 3/4 Embedding frontend into wwwroot...
xcopy /e /i /y web\dist publish\wwwroot

echo [package] 4/4 Writing start script...
(
echo @echo off
echo rem LevelForge one-click start
echo cd /d %%~dp0
echo echo Starting LevelForge on http://localhost:5000 ...
echo start http://localhost:5000
echo LevelForge.Platform.exe --urls http://localhost:5000
echo pause
) > publish\start.cmd

echo ==== Package complete: publish\ ====
pause
