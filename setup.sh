#!/usr/bin/env bash
# 新设备一键部署：构建解决方案+前端+全量测试 → 构建并部署玩法插件 DLL → 打印启动清单
# （插件 DLL 不进 Git：本脚本从仓库源码构建并放入 data/plugins/，平台启动时自动装载）
set -uo pipefail
cd "$(dirname "$0")"

echo "[1/3] 构建解决方案 + 前端 + 全量测试（build.sh）..."
bash build.sh || { echo "构建/测试未全绿——先解决再部署"; exit 1; }

echo "[2/3] 构建并部署玩法插件 DLL..."
bash deploy-plugins.sh || { echo "插件部署存在失败项（见上）"; exit 1; }

echo "[3/3] 完成。启动清单："
cat <<'EOF'
  1. ./run.sh 启动平台（http://localhost:5000）
  2. 设置页填写 LLM 地址/Key/模型（出题功能需要；求解/质检不需要）
  3. 如新设备上有游戏工程：向导①/设置里把关卡根目录指向工程内关卡目录后重新导入语料
  4. 门2 真机对拍需要游戏工程本体（按 docs/新设备继续任务.md 口径）
EOF
