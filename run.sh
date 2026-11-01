#!/usr/bin/env bash
# LevelForge 一键启动（Git Bash / Linux / macOS）
# 自动补用户级工具链 PATH；首次运行自动构建前端产物；随后启动平台并监听 http://localhost:5000
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$HOME/nodejs:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

if [ ! -d web/dist ]; then
  if command -v node >/dev/null 2>&1; then
    echo "[run] 首次运行：构建前端产物（约半分钟）..."
    (cd web && { [ -d node_modules ] || npm install --no-audit --no-fund; } && npm run build)
  else
    echo "[run] 提示：未安装 node，跳过前端构建；平台仍会启动但浏览器无页面（仅 API 可用）。"
  fi
fi

echo "[run] 启动平台：http://localhost:5000  （停止请按 Ctrl+C）"
dotnet run --project src/LevelForge.Platform --urls http://localhost:5000
