# PiggyFarm 适配层（路径 B 产物快照）

> 这是 PiggyFarm（Unity / ClassicMode）求解器适配层的源码快照，便于在另一台机器继续任务。
> 游戏工程本身**没有**被修改；适配层按路径 B 放在工具仓库之外，这里只是把它纳入版本管理。

## 目录

- `LevelForge.Adapters.PiggyFarm/`：插件工程（`IGameFacade` + 七成员适配器 + Boom 规则 + composer）
- `LevelForge.Adapters.PiggyFarm.Tests/`：35 个 xUnit 测试（规则/解析/门1/composer/类型/渲染契约）
- `PiggyFarmTemplates.cs`：内嵌真实关卡模板（374 / 19 / 29 / 59 + 112 猪密集模板）

## 构建与测试

```bash
cd adapters/piggyfarm
dotnet build LevelForge.Adapters.PiggyFarm/LevelForge.Adapters.PiggyFarm.csproj -c Release
dotnet test  LevelForge.Adapters.PiggyFarm.Tests/LevelForge.Adapters.PiggyFarm.Tests.csproj -c Release
```

`ProjectReference` 已改为相对路径（`../../../src/...`），新机器克隆工具仓库即可编译。

语料测试需要游戏工程；路径通过环境变量覆盖：

```bash
# Windows PowerShell
$env:PIGGYFARM_PROJECT_ROOT="D:\path\to\piggyfarm"
```

未设置且默认路径不存在时，语料测试会自动跳过（其余 34 个测试仍运行）。

## 部署到平台

1. 构建 Release，取 `LevelForge.Adapters.PiggyFarm.dll`；
2. 停平台（插件 DLL 被占用时上传会 409）；
3. 放到 `src/LevelForge.Platform/data/plugins/piggyfarm/`；
4. 启动平台 → 接入向导③上传/检测，或直接重启后自动装载。

## 当前支持范围（v1）

- 猪类型：Normal / Long / RichPig / **Boom**；其余 14 种特殊猪明确拒绝；
- 爆炸猪：碰撞扣计数，归零判负；状态键/快照/语义投影带 counter；
- 生成：2 档用原生 2 档模板 374；3 档用原生 3 档模板 19/29/59（需求 >96 用 112 猪密集模板）；
  按种子做对称 + 随机删猪（有类型要求时不删要求的类型）；Boom 需求把 Normal 转 Boom；
- 类型需求：支持「N只猪」「N只<类型>猪」，别名见 `PiggyFarmPigTypes`；
- 难度阈值：`[20,100]`（与项目原生 difficulty 对齐）。

## 未完成

- 其余 14 种特殊猪（弹簧/定时爆炸/锁/农场主/气球/捆绑/剪刀/鸭子/睡觉/铁笼/链接/丧尸/松鼠/兔子）；
- 真实关变异器/rollout 难度（工具通用方案见工作区 `工具通用优化方案.md`）。
