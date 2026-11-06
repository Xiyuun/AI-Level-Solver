# LevelForge 四轮：玩法代码全量接入 + 向导双路径 + 预算可配置 — 对话纪要

> 生成目的：供新对话（含跨设备）接续。
> 涉及工程/路径：
> - 工具仓库：`D:\notes\tongqu\ai-glm\LevelForge_glm`（已推送远端，见 §7）
> - 用户游戏项目：`E:\Projects\UnityProjects\PiggyFarm\piggyfarm`（Unity，piggyfarm 玩法）
> - 平台数据目录：`src/LevelForge.Platform/data/`（gitignore，本机独有，跨设备必须手动拷）
> 前序纪要：`2026-11-06_适配层插件化与向导项目化_LevelForge_glm.md`（三轮内容，本机 TempDoc 目录）
> 整理日期：2026-11-06

## 1. 本轮结论（先读）

| 项 | 结论 |
|---|---|
| 起点 | 用户实测三轮的②深度分析：抽样 8 文件全是 UniTask 插件代码，四问全"不确定"——抽样策略系统性漏掉玩法代码 |
| 用户拍板原则 | ①插件/三方库一律忽略；②玩法代码（规则承载）必须**全量获取**——少一段都会误解规则、拖垮求解器质量 |
| 落地 | SampleCode 重写两层结构（全量索引+玩法目录全文）；games v5 加 code_root；向导①"玩法代码根目录"用户确认；②双路径引导；卫生度分级+工作量预估补进平台分析 |
| 预算修正（用户指正） | 原 48K/64K 字符预算基于"顶级模型 200K 窗口"的**过时假设**；用户模型 deepseek-v4.1-flash-1017 实为 1M 窗口 → 预算改可配置，默认分析 200K / 生成 300K 字符 |
| 实测效果（piggyfarm 200K） | 证据从文件级升到**方法签名级**；机制 8 条/风险 7 条全部带真实文件证据；卫生度=纠缠（规则寄生在 MonoBehaviour Update/物理 Trigger），工作量 16~23 人日 |
| 顺手修复 | D9 遗留：ExtractFiles 解析不了自己的 FILE 标记（尾部 ==== 未剥离）——路径 A 文件提取链路首次真正修通 |
| 提交状态 | 全部已提交并推送：`d3fe893`（round4）+ 本纪要，本地=远端 |

## 2. 已落地 / 当前状态

### 提交
```
<本纪要提交> docs: round4 handoff summary
d3fe893 round4: gameplay code fulltext for analysis/gen - code_root + configurable budgets + dual-path wizard step2
2c6312e docs: PROGRESS 10.8（更早为三轮链）
```
远端：`https://github.com/Xiyuun/AI-Level-Solver.git`（master 已同步）。

### 后端改动
| 文件 | 内容 |
|---|---|
| `Storage/ProjectScanner.cs` | 加 `CodeDirCandidate`（祖先子树计数排名——纯容器目录不隐身）；`CollectSourceFiles` 深度 12；skip 名单加 plugins/thirdparty；`SourceRoot()`；画像加 `CodeDirCandidates` |
| `Storage/ProjectAnalyzer.cs` | `SampleCode` 重写两层：索引层（≤500 文件：路径·行数·类/public 签名）+ 正文层（codeRoot 下全文收录，预算封顶+诚实标注收录 N/M）；`ResolveCodeDir`（用户确认优先/自动提名如实标注）；`ProjectAnalysis` 加 `HygieneGrade`/`WorkEstimate`；系统提示词加两字段与判据（对齐 skills/onboarding-skill/qualification.md）；`MaxAnalysisTokens` 16384；预算读 `analyze.codeBudgetChars`（默认 200K，钳 20K~2M） |
| `AdapterGen/AdapterGenRunner.cs` | 生成预算读 `gen.codeBudgetChars`（默认 300K）；`ExtractFiles` 修复（文件名剥离尾部 `====`） |
| `Database/AppDatabase.cs` | 迁移 v5：games 加 `code_root` |
| `Database/GameStore.cs` | GameRow.CodeRoot + `UpdateCodeRoot` |
| `Api/*` | `PUT /api/games/{id}/code-root`；GameDetailDto/ProjectAnalysisDto 带新字段 |

### 前端改动（web/）
- ①：玩法代码根目录候选（单选）+ 手填 + 保存；②：双路径引导（路径 A 平台内分析 / 路径 B 复制需求包→外部 AI→报告粘贴回文本框存档，DLL 在③上传）；「重新深度分析」按钮恒显（标注上次分析时间）；报告文本框刷新自动回填（已存档报告优先，其次分析初稿）；「把清单初稿写入④」按钮旁内联反馈（原成功消息渲染页首视口外且④锁定，观感"没反应"）；需求包内嵌 codeRoot+候选
- `Settings.tsx`：新增"深度分析/平台内生成·玩法代码正文预算"两字段 + 按窗口调参提示

### 测试
后端 286（GameplayCodeTests ×5：候选跳过插件/索引+全文/自动提名标注/预算封顶诚实标注/code-root 落库；修 DatabaseAndSeedTests v5、AdapterGenTests 夹具改 FILE 标记格式、ProjectAnalysisTests 卫生度字段）+ 前端 75，全绿。

### 平台服务与数据（本机状态）
- 服务运行中（run.cmd，端口 5000）；piggyfarm：codeRoot=Assets/Main/Game 已存档；最新一轮 200K 分析已落库（纠缠/16~23 人日/机制 8/风险 7）；②文本框已自动回填初稿**但尚未「存档并完成」**

## 3. 关键决策与约定

| 决策 | 内容 |
|---|---|
| 玩法代码边界由用户画 | 向导①确认 code_root，工具只提名候选不猜语义（诚实语义） |
| 索引层零遗漏 | 全部非插件源码文件进索引（签名级）——"少一点都不行"在索引层成立；正文层按预算尽力全文 |
| 预算按窗口配置 | 预算是"按过时窗口假设定值"的教训 → 可配置 + 设置页注明"1M 窗口建议 20万~40万；小窗口过大反而截断" |
| 上下文窗口不以偏概全 | 教训：不核实当前型号规格就不要泛化行业概况（用户指正 200K 说法错误） |
| 诚实语义不可破 | 初稿≠已确认、未知照实、预算外如实标注、受阻不冒充完成 |
| 双路径并存 | 路径 A（策划：平台内分析/生成闭环）与路径 B（程序：需求包→外部 AI→报告回②/DLL 上③）都必须完整可用 |

## 4. 参考项目 / 调研摘要

- piggyfarm 代码结构（对适配层有用）：`Assets/Main/Game/`（332 文件，规则承载）；Modules/ClassicMode/Script/{Controller/Pigs,DataModel,Manager/MainGame}；`FrameWork/` 基础设施、`FrameWorkExtras/{Config,LevelData}` 规则邻接
- 关键类：NormalPig(2519行, IPointerClickHandler)/PigManager(2410)/GameManager(2709)/MapManager(601)/ToolManager(1584)/BoardDeadlockDetector/ClassicProgressCodec(EncodeSnapshot/TryDecodeBlob)/ClickUndoHistory(ClickSnapshot+PigUndoExtras)/ClassicLevelBinaryCatalog(Luban 分块)
- 项目里已有门2对拍 harness：`Assets/Editor/LevelForgeHarness/PairingHarness.cs`（358 行）——门2驱动可直接从它起步
- 深度分析判定：卫生度=纠缠（规则状态寄生 MonoBehaviour Update/物理 Trigger）；回合制/确定性 uncertain 的**实质原因**是物理耦合与概率字段（ConfrontEnemyGroup.probability 无种子控制），非"没读到"

## 5. 已知问题与操作注意

| 问题 | 状态/注意 |
|---|---|
| 关卡根目录仍是错的 | ①里 levels_root 还停在 `ServerData/WebGL/`（实为 Addressables 目录）。分析证实关卡是 **Luban 二进制 ByteBuf 经 Addressables 分块加载**——接入前需找到 Luban 导出表产物（或决定直接解码二进制），修正①的关卡根目录 |
| 200K 预算仍非全量 | Game 全文约 2.2M 字符，200K 只覆盖核心链路（用户可自行调大到 ~40 万，1M 窗口内）；预算外文件以索引兜底+如实标注 |
| 服务运行锁 DLL | 重新编译平台必须先停实例（taskkill LevelForge.Platform.exe）；run.cmd 有端口预检 |
| 跨设备接续 | `data/` 目录不随 git 走：DB（含 code_root/分析结论/向导存档/**明文 API key**）+plugins+tapes 都在里面——拷贝到新设备同路径即完美还原；不拷则需重新导入+重新分析（花真实 token） |
| 游戏项目绝对路径 | 库里 project_root 是本机绝对路径 `E:\...`——新设备路径不同时需重新导入 piggyfarm |
| ②尚未存档 | 最新 200K 分析只是初稿，用户还没点「存档并完成」（文本框已自动回填，审核后存档） |

## 6. 新对话可继续的任务

- [ ] piggyfarm 接入续：找 Luban 表产物（outputDataDir）/确认关卡数据形态 → 修正①关卡根目录 → 关卡导入（Luban ByteBuf 可能需要解码方案）
- [ ] ②审核当前初稿并「存档并完成」（可改写后再存）
- [ ] piggyfarm 适配层（优先路径 B）：从 `src/Adapters/LevelForge.Adapters.SlidePig` + `77f6cf7` ILevelComposer WIP 起步；GameId=piggyfarm；需先解决 Luban ByteBuf 关卡解码
- [ ] 门2对拍：项目内已有 `Assets/Editor/LevelForgeHarness/PairingHarness.cs`，登记驱动命令/参数（参考 Gate2DriverConfig 占位符 {levelFile}/{tapeFile}/{outputFile}）
- [ ] 确定性风险处置：概率字段（ConfrontEnemyGroup）种子控制、物理 Trigger 推进的格子化纯规则层重构——适配层前置决策
- [ ] （可选）模型侧路径 A 改进继续观察：更强的代码生成仍是 4 轮编译上限约束（交接前序 §1）

## 7. 关键路径索引

| 路径 | 说明 |
|---|---|
| `https://github.com/Xiyuun/AI-Level-Solver.git` | 远端仓库（master，与本地同步） |
| `D:\notes\tongqu\ai-glm\LevelForge_glm` | 工具仓库本地（本机）；新设备 clone/pull 即可编译开发 |
| `src/LevelForge.Platform/data/` | 平台数据（gitignore；**跨设备手动拷**；含明文 API key） |
| `run.cmd` | 一键启动（需 .NET 10 SDK + Node；首次自动构建 web） |
| `PROGRESS.md` §10.9 | 本轮落地记录（与纪要互补） |
| `web/src/pages/Onboarding.tsx` / `Settings.tsx` | 向导（双路径/重新分析/回填）/设置页（预算字段） |
| `src/LevelForge.Platform/Storage/ProjectAnalyzer.cs` | SampleCode 两层结构 + 预算 + 卫生度字段 |
| `src/LevelForge.Platform/Storage/ProjectScanner.cs` | 代码目录候选（祖先子树计数）+ 源码收集 |
| `E:\Projects\UnityProjects\PiggyFarm\piggyfarm` | 游戏项目本体（本机；新设备需自行同步） |
| `skills/onboarding-skill/qualification.md` | 资格评估报告格式（卫生度判据与工作量上浮规则） |

---
*文档版本：基于 2026-11-06 对话整理（四轮：玩法代码全量 + 向导双路径 + 预算可配置）。*
