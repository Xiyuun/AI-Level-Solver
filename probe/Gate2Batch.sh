#!/usr/bin/env bash
# 门2批量对拍驱动：20 关 = DumpTape 制带 → _gate2copy batchmode 执行 → CompareTape 比对（全 ASCII 命令行）
set -u
REPO="D:/notes/tongqu/ai-glm/LevelForge_glm"
LEVELS_DIR="E:/Projects/UnityProjects/PiggyFarm/piggyfarm/Assets/Main/FrameWorkExtras/LevelData/Luban~/Datas/Levels"
UNITY="/c/Program Files/Unity/Hub/Editor/2022.3.62f3c1/Editor/Unity.exe"
COPY_PROJ="E:\\Projects\\UnityProjects\\PiggyFarm\\_gate2copy"
WORK="$TEMP/levelforge-gate2-batch"
mkdir -p "$WORK"

# 抽样20关：覆盖全部机制类型与规模梯度（1示例 + Normal/Long/Lock/Cage/Boom/Spring + 大中小规模）
LEVELS="000001 000002 000003 000004 000015 000021 000031 000032 000033 000036 000047 000056 000062 000064 000071 000075 000077 000085 000089 000110"

pass=0; fail=0; error=0
for lv in $LEVELS; do
  dir="$WORK/$lv"
  mkdir -p "$dir"
  # ① 制带（门面口径：稀疏A*/稠密阶梯）
  if ! (cd "$REPO" && dotnet run --project probe/DumpTape -- "$LEVELS_DIR/$lv.json" "$dir" > "$dir/dump.log" 2>&1); then
    echo "$lv DUMP-ERROR"; error=$((error+1)); continue
  fi
  if [ ! -f "$dir/tape.json" ]; then
    echo "$lv DUMP-UNSOLVED"; error=$((error+1)); continue
  fi
  # ② 真机执行（副本 batchmode；超时10分钟兜底）
  timeout 600 "$UNITY" -batchmode -nographics -projectPath "$COPY_PROJ" \
    -executeMethod LevelForgeHarness.PairingHarness.Run \
    --level "$(cygpath -w "$dir/level.json")" \
    --tape "$(cygpath -w "$dir/tape.json")" \
    --out "$(cygpath -w "$dir/result.json")" \
    -logFile "$(cygpath -w "$dir/unity.log")" > /dev/null 2>&1
  # ③ 比对
  if [ ! -f "$dir/result.json" ]; then
    echo "$lv RUN-ERROR (no result.json)"; error=$((error+1)); continue
  fi
  verdict=$(node "$REPO/probe/CompareTape.mjs" "$(cygpath -w "$dir/result.json")" "$(cygpath -w "$dir/tape.json")" 2>/dev/null | tail -1)
  if echo "$verdict" | grep -q "一致"; then
    echo "$lv PASS"; pass=$((pass+1))
  else
    echo "$lv FAIL: $verdict"; fail=$((fail+1))
  fi
done
echo "==== GATE2 BATCH: pass=$pass fail=$fail error=$error ===="
