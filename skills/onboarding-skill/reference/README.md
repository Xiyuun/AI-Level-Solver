# reference/ · 参考实现

> **类比迁移的锚点，不是复制模板**：参考它的结构与测试写法，规则必须从项目源码重新推导（README 明示）。

## 开源默认：玩具推箱子（本仓库内完整实现）

| 产物 | 仓库位置 |
|---|---|
| 适配层（七成员 + 执行协议） | `src/Adapters/LevelForge.Adapters.SokobanToy/SokobanToyAdapter.cs` |
| 格式校验器 | `src/Adapters/LevelForge.Adapters.SokobanToy/SokobanToyValidator.cs` |
| 格式说明书 / 机制表 / 视觉映射 / 玩法文档 / 规则矩阵 / 预制关 | `src/Adapters/LevelForge.Adapters.SokobanToy/SokobanToyContent.cs` |
| 适配层测试（通用骨架的活样例） | `tests/LevelForge.Adapters.SokobanToy.Tests/` |
| 平台侧门面（登记形态） | `src/LevelForge.Platform/Facades/SokobanToyFacade.cs` |
| 内核契约正文（签名） | `src/LevelForge.Kernel/Contracts/` |

玩具预制 10 关用途：toy-001 示例（few-shot）、toy-002~009 回归（预期 solved）、toy-010 已证无解用例（预期 unsolvable_proven——穷尽证明而非规则判定）。

## 使用定位

- 玩具兼任：skill 参考、安全演示路径、零接入体验
- 玩具**无独立游戏本体**：门2不适用（不能显示"真实游戏对拍通过"）；基准预期由工具团队人工穷举确认
- 预制关不作为充分的难度校准数据

## 内部版：滑动猪完整实现（含 Unity harness，2026-11-03 实战定稿）

> **经真实项目全流程检验的"标准答案"**：v1 核心子集（8 类型，75/110 关），门1 五项全绿，
> 门2 真机对拍 17/20 全绿（1 关为已查明的物理停格回退模型边界，规则矩阵附证据），
> 门3 全量基准见平台库。以下产物即按本 skill 作业指导真实走完一遍的成果：

| 产物 | 仓库位置 |
|---|---|
| 适配层（七成员 + 执行协议 + A*启发式能力） | `src/Adapters/LevelForge.Adapters.SlidePig/SlidePigAdapter.cs` |
| 纯规则引擎（网格模型，与游戏语义对应见规则矩阵） | `src/Adapters/LevelForge.Adapters.SlidePig/SlidePigRules.cs` |
| 格式校验器（含 v1 子集判定） | `src/Adapters/LevelForge.Adapters.SlidePig/SlidePigValidator.cs` |
| 五件接入产物（规则矩阵带源码证据/视觉映射/机制表/玩法文档/格式说明书） | `src/Adapters/LevelForge.Adapters.SlidePig/SlidePigArtifacts.cs` |
| 适配层测试（22 个：规则语义逐条 + 真实关夹具 + 门1） | `tests/LevelForge.Adapters.SlidePig.Tests/` |
| 平台门面（稀疏 A*/稠密贪心阶梯策略选择） | `src/LevelForge.Platform/Facades/SlidePigFacade.cs` |
| Unity harness（batchmode 真机对拍，实战检验版） | `harness-templates/unity-batchmode/PairingHarness.cs`（同步于猪项目 `Assets/Editor/LevelForgeHarness/`） |

### 实战经验（接入新项目时优先读）

1. **游戏自带文档优先**：PiggyFarm 的 AGENTS.md/GAMEPLAY.md 让规则提取快了一个量级——先找项目记忆文档。
2. **普查先行**：接入前先扫关卡数据分布（类型/实体数/机制），v1 子集按数据划界而非按规则书全集。
3. **超稠密游戏需要非最优搜索**：61~122 实体的关卡精确搜索不可行，机动性排序+腾挪预算阶梯的贪心 DFS
   把可解覆盖从 1/75 提到 31/75（求解内核 §10 优化路径的真实触发）。
4. **门2 会抓到读代码抓不到的东西**：铁笼回收时序、物理停格回退（贴靠格被占向后调整）——
   两者都不在任何文档里，前者修在 harness 采样口径上，后者作为模型边界落档规则矩阵。
5. **harness 三坑**（详见 PROGRESS 坑表 13-15）：PlayMode domain reload 用 SessionState 续跑；
   Unity 2022 是 netstandard2.1 档（ToHexString/HashData 不可用）；语义哈希必须与平台规范化口径逐字节对齐。
