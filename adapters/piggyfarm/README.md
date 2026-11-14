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

## 当前支持范围（v1.2）

- 猪类型：Normal / Long / RichPig / **Boom**（碰撞扣计数，归零判负）/ **Duck 小黄鸭**（1x1，任一离场/碰撞事件后 BFS 寻路自动离场，鸭不挡鸭）/ **Zombie 丧尸猪**（= 普通猪 + 感染标记：与普通猪的每次已结算接触会把普通猪转为丧尸，仅影响结算奖励，不影响胜负与走位）；其余特殊猪明确拒绝；
- 被挡的猪可**原地顶撞**（起步即碰撞，触发事件但不位移——与游戏点击语义一致）；
- 生成：2 档池 = Level374 / DuckB2(10鸭) / 424B2(纯普通97) / 024B2 / 014B2 / 194B2(Boom) / BoomB2(97猪16爆)；3 档池 = Level19/29/59 / Dense112 / DuckB3(110猪30鸭) / 469B3(纯普通91)；
- 类型纯度：有类型需求时只选类型 ⊆ {普通猪+要求类型} 的模板（需求即契约，未要求的特殊猪不出现）；无类型需求时出真实关卡原貌（质量门把关）；
- 语料覆盖：499 个编辑源中支持 **175 关**（v1.1 为 163）；时间类（TimeBoom/JokerBox）诚实拒绝——真实时间驱动无法回合化，强行转译会导致解法与真机不一致；
- 注意：游戏工程 2026-11-09 后有更新——语料中 Lock/Link/Zombie 已被团队清零，新增 **IcePig 冰猪（92 实例，未建模的最大类型）**、ClockworkPig、BalloonLong；冰猪建模是下一优先级；
- 难度阈值：`[20,100]`（与项目原生 difficulty 对齐）。
## 未完成

- 其余 14 种特殊猪（弹簧/定时爆炸/锁/农场主/气球/捆绑/剪刀/鸭子/睡觉/铁笼/链接/丧尸/松鼠/兔子）；
- 真实关变异器/rollout 难度（工具通用方案见`docs/工具通用优化方案.md`）。
