#!/usr/bin/env bash
# 构建并部署全部玩法插件 DLL 到平台 data/plugins/（新设备初始化 / 适配层更新共用）
# 注意：平台运行中时 DLL 会被装载锁占用——请先停止平台（关闭 run 窗口 / 结束 LevelForge.Platform 进程）再执行
set -uo pipefail
cd "$(dirname "$0")"

PLUGINS="src/LevelForge.Platform/data/plugins"
fail=0

echo "[mahjong-260417] 构建 Release..."
dotnet build adapters/mahjong/LevelForge.Adapters.Mahjong/LevelForge.Adapters.Mahjong.csproj -c Release --nologo -v q \
  || { echo "[mahjong-260417] 构建失败"; fail=1; }
mkdir -p "$PLUGINS/mahjong-260417"
if cp adapters/mahjong/LevelForge.Adapters.Mahjong/bin/Release/net10.0/LevelForge.Adapters.Mahjong.dll \
      "$PLUGINS/mahjong-260417/" 2>/dev/null; then
  echo "[mahjong-260417] 已部署"
else
  echo "[mahjong-260417] 拷贝失败：DLL 被运行中的平台占用——请先停止平台再重新执行本脚本"
  fail=1
fi

echo "[piggyfarm] 构建 Release..."
dotnet build adapters/piggyfarm/LevelForge.Adapters.PiggyFarm/LevelForge.Adapters.PiggyFarm.csproj -c Release --nologo -v q \
  || { echo "[piggyfarm] 构建失败"; fail=1; }
mkdir -p "$PLUGINS/piggyfarm"
if cp adapters/piggyfarm/LevelForge.Adapters.PiggyFarm/bin/Release/net10.0/LevelForge.Adapters.PiggyFarm.dll \
      "$PLUGINS/piggyfarm/" 2>/dev/null; then
  echo "[piggyfarm] 已部署"
else
  echo "[piggyfarm] 拷贝失败：DLL 被运行中的平台占用——请先停止平台再重新执行本脚本"
  fail=1
fi

if [ "$fail" -ne 0 ]; then
  echo "插件部署存在失败项：停止平台后重新执行 deploy-plugins.sh 即可"
  exit 1
fi
echo "全部插件已部署。启动平台（run.sh / run.cmd）后会自动装载并运行门1 自检。"
