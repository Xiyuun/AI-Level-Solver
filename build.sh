#!/usr/bin/env bash
# LevelForge 一键构建+测试（Git Bash / Linux / macOS）
set -euo pipefail
cd "$(dirname "$0")"

dotnet build LevelForge.slnx -c Debug
dotnet test LevelForge.slnx -c Debug --no-build
