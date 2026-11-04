# 第 17 步 · 故障注入实验对照表 + 演示证据

> 日期：2026-11-04。每行 = 一个主动注入的缺陷 → 预期防线 → 实际结果。演示时明确标注"人工注入故障"。

## 故障注入对照表

| # | 注入的缺陷 | 注入方式 | 预期防线 | 实际结果 | 证据 |
|---|---|---|---|---|---|
| 1 | StateKey 漏计数维度 | `SlidePigFaultAdapter(StateKeyIgnoresCounters)` 装饰器 | 门1 键判别测试 | ✅ 红灯并点名"爆炸计数"维度 | `SlidePigFaultInjectionTests.故障1` |
| 2 | 漏枚举操作（奇数猪冻结） | `SlidePigFaultAdapter(OddPigsFrozen)` | 门3 基准 diff | ✅ 1 号关从 solved(6步) 翻转为 unsolvable_proven（穷尽证明） | `SlidePigFaultInjectionTests.故障2` |
| 3 | 爆炸规则反转（碰撞不扣计数） | `SlidePigFaultAdapter(BoomNeverDecrements)` | 门2 逐步语义比对 | ✅ 真机第 47 步首分歧：pig#13 counter harness=2 vs tape=3（真游戏扣了，故障磁带没扣） | 本目录 4 个文件 + `SlidePigFaultInjectionTests.故障3` |
| 4 | harness 绕过真实输入路径 | 文档演示：假对拍用拷贝磁带数据替代真执行 | 人工审核清单第 12 条 | 结构性覆盖：机器验证无法发现（这是④人工审核不可省的原因） | 《接入套件skill详设》§4.3 假对拍判定标准 |
| 5 | 生成候选伪装成功（难度不符却放行） | 门1 键判别/纯函数/确定性测试（第 5 步的四缺陷测试双） | 门1 全部五项 | ✅ 四类注入缺陷全部被对应检查抓到（PurityAdapter/SyntheticFailure 测试双） | `ComplianceChecksTests` + `DeterminismCheckTests`（第 5 步产出） |

## 故障③真机实验细节（门2 逐步语义比对的实战）

- 故障：适配层宣称爆炸猪碰撞**不扣**计数（规则反转）→ 用故障适配层求解 71 号关（106 实体含爆炸猪）→ 138 步解 → 制带
- 真机：`_gate2copy` batchmode Unity 加载同一关，沿磁带 138 步逐步执行真实点击
- 比对：初始 + 前 46 步全部一致（哈希级）→ **第 47 步首分歧**：`pig#13 counter: harness=2 vs tape=3`
  - harness（真游戏）：爆炸猪 #13 被撞后计数从 3 扣到 2（游戏正确行为）
  - tape（故障适配层）：宣称计数仍为 3（反转规则）
- 结论：**门2 的逐步语义比对在真实游戏上抓住了规则反转**——这就是"AI 做错了怎么被抓住"的活案例

## 各文件说明

| 文件 | 内容 |
|---|---|
| `level.json` | 71 号关原始数据（Luban JSON） |
| `tape.json` | 故障适配层产出的 138 步磁带（含错误的爆炸计数语义） |
| `result.json` | 真游戏 harness 实报（第 47 步 pig#13 counter=2） |
| `fault3-compare-output.txt` | CompareTape 比对输出（首分歧步 47） |

## 关联测试

- `tests/LevelForge.Adapters.SlidePig.Tests/SlidePigFaultInjectionTests.cs`：4 个测试覆盖基准+三故障
- `tests/LevelForge.Kernel.Tests/Hosting/`：第 5 步的四缺陷测试双（对应表中 #5 的详证）
