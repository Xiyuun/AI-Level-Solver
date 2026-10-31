# deliverables.md · 产出物清单与 MANIFEST 格式

> 全部产物落 `onboarding-output/`（相对项目根）。每完成一件立即更新 MANIFEST（README 守则 3）。

## 目录结构与每件格式要求（对应门1验收）

```
onboarding-output/
  MANIFEST.json          # 产物清单：{name, path, status, contentHash, version, note}[]
  qualification-report.md
  rule-matrix.md         # 开发者确认的规则矩阵，关联状态维度/测试/对拍样本
  move-schema.json       # 结构化动作字段、版本及编码/解码约定
  semantic-schema.json   # 语义状态字段、版本、实体对应关系与规范化规则
  adapter/               # 适配层代码（七成员 + 执行协议）
  adapter.tests/         # 单元测试（通用骨架 + 项目边界用例）
  format-spec.md         # 格式说明书（项目关卡格式 → 给 LLM 提示词的描述文档）
  format-validator.*     # 输出校验器（校验生成 JSON 是否符合项目格式）
  mechanics-table.json   # 机制解锁表（编号范围 → 可用机制）
  visual-mapping.json    # 视觉映射声明（渲染规范 §3）
  game-brief.md          # 玩法描述文档（生成端提示词的规则书）
harness/                 # 可选打包副本；实际入口在测试工程 Assets/Editor/LevelForgeHarness/ 等可编译位置
```

## MANIFEST.json 格式

```json
[
  {
    "name": "adapter",
    "path": "onboarding-output/adapter/",
    "status": "done",
    "contentHash": "sha256:…（目录内全部文件字节规范化后的摘要）",
    "version": "1.0.0",
    "note": "含维度覆盖对照表；REVIEW 3 处已解决 2 处"
  }
]
```

- 规则矩阵、动作/语义 schema 归入适配及测试产物组，**不另造平台关卡格式**
- MANIFEST 列出这些文件及实现文件的指纹；修改后验证记录依赖失效规则与《验证门》§2.1 一致
- 运行资源条件保存在任务输入中并形成 runConfigHash；harness 按实际配置核对，**不能只回显请求中的哈希**

## 合入平台（人工步骤，不属于 agent 作业）

1. 审核（checklist 13 条 + rule-matrix 逐行）通过
2. 适配层文件拷入平台仓库 `src/Adapters/LevelForge.Adapters.<Game>/`
3. `GameRegistry` 登记新门面（静态注册，冻结决策）
4. 平台向导①登记门2驱动配置 → ③⑤⑥ 跑三道门 → 上线注册（试用档/转正档）
