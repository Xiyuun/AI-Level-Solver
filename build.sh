#!/usr/bin/env bash
# LevelForge 一键构建+测试（Git Bash / Linux / macOS）
set -euo pipefail
cd "$(dirname "$0")"

# 用户级工具链（新设备装在 ~/.dotnet 与 ~/nodejs，不动系统安装）
export PATH="$HOME/.dotnet:$HOME/nodejs:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

dotnet build LevelForge.slnx -c Debug
dotnet test LevelForge.slnx -c Debug --no-build

# 前端：存在 package.json 且 node 可用时跑 vitest（依赖缺失先安装）
if [ -f web/package.json ] && command -v node >/dev/null 2>&1; then
  if [ ! -d web/node_modules ]; then
    (cd web && npm install --no-audit --no-fund)
  fi
  (cd web && npm run test)
fi
