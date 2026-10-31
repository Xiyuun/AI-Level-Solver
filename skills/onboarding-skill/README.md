# LevelForge 接入套件 skill（onboarding-skill）

> 版本：1.0（与平台 contractVer=1、磁带 tape v1 配套）
> 本目录是给**编码 agent** 读的作业指导书：按它把一个新游戏项目接入 LevelForge（产出适配层，不是产出求解器）。
> 人（项目开发者）只用《checklist.md》与《replay-spec.md》审核。

## 1. 加载方式（工具无关）

- **ZCode**：将本目录放入技能目录（或项目 `.zcode/skills/onboarding-skill/`），会话开场引用本 README。
- **Cursor**：将本目录加入 Cursor Rules（`.cursor/rules/onboarding-skill/`，逐文件引用）。
- **Claude Code**：放入 `.claude/commands/onboarding/`，斜杠命令加载本 README 与当前阶段文件。

## 2. 两阶段运行（先资格、后生成）

1. **阶段一·资格评估**：读《qualification.md》，扫项目代码回答四问，产出 `onboarding-output/qualification-report.md`。**四问任一拒绝即停**，不做后续。
2. **阶段二·适配层生成**：读《onboarding.md》按七步工作流执行；**契约以《contract.md》为唯一正文**；对拍产物按《replay-spec.md》。
3. **断点续跑**：新会话第一步永远是——读 `onboarding-output/qualification-report.md` 与 `onboarding-output/MANIFEST.json` 恢复上下文；缺件从《deliverables.md》清单对照补齐。

## 3. agent 行为守则（最重要，前置；违反即返工）

1. **语义不确定必须问人，禁止猜**。判定标准：该答案会改变 `GetLegalMoves / ApplyMove / IsGoal` 的输出吗？会 → 停下来问开发者，把问答记录进 rule-matrix.md。
2. **REVIEW 标注义务**：每处"从代码推断而非确证"的实现，打标注 `// REVIEW: 推断——依据…`。代码卫生度越差，标注密度越高。
3. **MANIFEST 义务**：每完成一件产物立即更新 `onboarding-output/MANIFEST.json`（格式见《deliverables.md》）。
4. **游戏发布逻辑零改动**：产物全部写 `onboarding-output/`；测试工程只允许添加 `Assets/Editor/LevelForgeHarness/` 脚本或既定测试程序集；写入路径记入 MANIFEST。若必须改发布逻辑才能接入 → 单列"扩展需求"上报，**不算默认路径**，禁止自行修改。
5. **诚实语义**：预算耗尽/未知不写成无解；测试通过不宣称"任意未来输入正确"；三视角（语义投影/显示快照/状态键）看不见的差异要在 rule-matrix 注明覆盖边界。
6. **skill 版本号**（本文件头部）写入报告元信息；平台据此判断 skill 与产物版本匹配。

## 4. 产物去向

全部产物落 `onboarding-output/`（清单见《deliverables.md》）；人工审核通过后由开发者把适配层文件合入平台仓库 `src/Adapters/`，并在 `GameRegistry` 登记——那一步不属于 agent 作业范围。

## 5. 参考实现

《reference/》指向本仓库的玩具推箱子完整实现（开源默认参考）。**类比迁移的锚点，不是复制模板**：参考它的结构与测试写法，规则必须从项目源码重新推导。
