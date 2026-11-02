#!/usr/bin/env bash
# LevelForge 一键构建+测试（Git Bash / Linux / macOS）
set -euo pipefail
cd "$(dirname "$0")"

# 用户级工具链：仅当用户级 dotnet 带 10.x SDK 时才优先（存在 ~/.dotnet 装旧版如 8.x 遮蔽系统 SDK 的机器）
# nodejs 目录不存在时 PATH 前置是无害空操作；无用户级 10.x 时回落系统 dotnet
export PATH="$HOME/nodejs:$PATH"
if [ -x "$HOME/.dotnet/dotnet" ] && ls "$HOME/.dotnet/sdk" 2>/dev/null | grep -qE '^10\.'; then
  export PATH="$HOME/.dotnet:$PATH"
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
fi

dotnet build LevelForge.slnx -c Debug
dotnet test LevelForge.slnx -c Debug --no-build

# 前端：存在 package.json 且 node 可用时跑 vitest（依赖缺失先安装）
if [ -f web/package.json ] && command -v node >/dev/null 2>&1; then
  if [ ! -d web/node_modules ]; then
    (cd web && npm install --no-audit --no-fund)
  fi
  (cd web && npm run test)
fi
