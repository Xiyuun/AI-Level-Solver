# replay-spec.md · 对拍 harness 规范

> harness 是测试工程内一个小执行器（Unity：`Assets/Editor/LevelForgeHarness/` 静态入口或既定测试程序集）。
> **发布逻辑零改动**；人工审核首先确认：加载、输入、状态提取、胜负均来自真实游戏。

## 4.1 职责与形态

- 产物包可另设 `harness/` 目录，但必须在 MANIFEST 声明**实际可编译入口**
- 模板起点：平台仓库 `harness-templates/unity-batchmode/PairingHarness.cs`

## 4.2 启动契约

```text
命令行：--level <原生关卡文件> --tape <含结构化move的磁带文件> --out <结果文件>
退出码：0 = 正常执行并有结果；非 0 = harness 自身失败（与游戏胜负无关）
超时：由平台侧 kill，harness 不自行处理
```

## 4.3 执行契约（假对拍的判定标准）

1. **加载**：走生产加载路径（与玩家进关同一代码路径）
2. **注入**：每个操作走真实输入入口（与玩家点击同一处理函数）；禁止直接改内部状态
3. **提取**：加载后提取初始语义状态；每步**等待业务结算完成后**记录实际 semanticState、semanticStateHash、动作接受状态、失败原因及游戏胜负。状态必须来自游戏本体，不从适配器计算；等待超时标记未完成，不能拿中间态当正常结果
4. **结束**：输出胜负（读游戏自身判定）与末局语义状态，保留已执行的完整步骤；缺步、重复步或未完成结算不能标为对拍通过

**任何一条不满足 = 假对拍，审核打回。**

## 4.4 输出格式（result.json）

```json
{
  "levelRef": "217", "levelHash": "sha256:...",
  "gameBuildFingerprint": "...", "harnessHash": "sha256:...",
  "moveSchemaVer": 1, "semanticSchemaVer": 1,
  "runConfigHash": "sha256:...", "executionEnvironment": "unity-editor-batchmode",
  "initial": {
    "semanticState": { "entities": [{ "id": "box-A", "x": 2, "y": 2 }], "globals": { "remainingMoves": 12 } },
    "semanticStateHash": "sha256:..."
  },
  "steps": [
    {
      "index": 1,
      "move": { "type": "push", "entityId": "box-A", "direction": "up" },
      "accepted": true, "settled": true, "reason": null,
      "semanticState": { "entities": [{ "id": "box-A", "x": 2, "y": 1 }], "globals": { "remainingMoves": 11 } },
      "semanticStateHash": "sha256:...", "outcome": "win"
    }
  ],
  "outcome": "win",
  "finalSemanticState": { "entities": [{ "id": "box-A", "x": 2, "y": 1 }], "globals": { "remainingMoves": 11 } }
}
```

- 示例只展示协议字段，实际玩法字段由 semanticSchema 定义；outcome ∈ win/lose/incomplete/error
- **levelHash 必须由实际加载文件字节计算**并与输入磁带核对，不能只回显请求值
- 每步同时保留语义对象与摘要（平台才能高亮首分歧步和具体字段）；规范化方法与四类摘要定义见《渲染器与视觉映射规范》§5.1

自动对拍比较：初始状态、动作序列完整性、每步结算状态与语义状态、最后游戏胜负。只输出胜负的人工降级记录注明证据强度，不提供逐步一致性结论。执行确认绑定当前 levelHash、游戏版本、harness 与运行条件。

## 4.5 引擎模板与常见坑

见平台仓库 `harness-templates/unity-batchmode/`（batchmode 模板骨架）。常见坑清单：

- 帧驱动结算需主动推进帧（batchmode 无主循环tick）
- 随机数须设种子（对拍可复现）
- 异步加载须等待完成信号再注入
- 编辑器模式与真机构建的平台 API 差异 → 用编辑器回落

## 4.6 人工对拍降级（项目无无头基建时）

- 平台/报告生成**可打印操作清单**：编号 + 逐步操作文字（DescribeMove 输出）+ 签认栏
- 开发照单执行（20 关中明确选 10 关），签认记录回填平台执行确认（人工降级模式，"仅最终结果"）
- 未打的关不算通过；报告不提供逐步语义一致结论

模板：`harness-templates/manual-pairing-checklist.md`
