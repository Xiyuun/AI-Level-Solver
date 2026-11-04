#!/usr/bin/env bash
# LevelForge 一键打包脚本：前端构建 + .NET self-contained 发布 + 嵌入 wwwroot + 启动脚本
# 运行：bash package.sh → 产物在 publish/ 目录（解压即用，无需安装 .NET）
set -euo pipefail
cd "$(dirname "$0")"

echo "[package] 1/4 构建前端..."
(cd web && npm run build)

echo "[package] 2/4 .NET self-contained 发布（win-x64）..."
rm -rf publish
dotnet publish src/LevelForge.Platform -c Release -r win-x64 --self-contained -o publish

echo "[package] 3/4 嵌入前端产物到 wwwroot..."
cp -r web/dist publish/wwwroot

echo "[package] 4/4 写入启动脚本与说明..."
cat > publish/start.cmd << 'SCRIPT'
@echo off
rem LevelForge one-click start (self-contained: no .NET SDK needed)
cd /d %~dp0
echo Starting LevelForge on http://localhost:5000 ...
start http://localhost:5000
LevelForge.Platform.exe --urls http://localhost:5000
pause
SCRIPT

echo "==== 打包完成：publish/ ($(du -sh publish | cut -f1)) ===="
echo "解压 publish/ 到任意 Windows x64 机器 → 双击 start.cmd → 浏览器自动打开"
