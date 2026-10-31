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

## 内部版：滑动猪完整实现（含 Unity harness）

> 待第 16 步产出（需滑动猪仓库/Unity 环境）。将包含：适配层、rule-matrix、Unity batchmode harness、对拍驱动配置——即本 skill 的"标准答案"。
