@echo off
rem LevelForge 一键构建+测试（Windows CMD）
cd /d %~dp0

dotnet build LevelForge.slnx -c Debug
if errorlevel 1 exit /b 1
dotnet test LevelForge.slnx -c Debug --no-build
