# LevelForge · 关卡求解与生成中台

AI 出题 + 确定性求解器验题的关卡生产线，配套"任何项目半天接入"的准入制度。

设计文档位于上级目录（`../方案书.md` 等 14 份，v0.2 修订版），本仓库为其实现。
实现计划共 19 步，见对话约定：阶段 A 内核与契约 → 阶段 B 平台后端 → 阶段 C 前端 → 阶段 D 真实项目接入 → 阶段 E 批量与发布。

## 仓库结构

```
LevelForge_glm/
├── LevelForge.slnx                  解决方案（.NET 10 新格式）
├── src/
│   ├── LevelForge.Kernel/           求解内核（契约层/搜索/回放/执行池/适配托管）
│   ├── Adapters/
│   │   ├── LevelForge.Adapters.SokobanToy/   内置玩具推箱子（开源默认参考实现）
│   │   └── LevelForge.Adapters.SlidePig/     滑动猪适配层（仅内部，第16步产出）
│   └── LevelForge.Platform/         单机 Web 平台（ASP.NET Core minimal API + SQLite）
├── tests/                           xUnit 测试（内核自测 / 适配层测试）
├── web/                             前端（React + Vite + TS，第12步起）
├── skills/                          接入套件 skill 九件套（第15步产出）
└── harness-templates/               对拍 harness 引擎模板（Unity 等，第15步产出）
```

## 构建与测试

前置：.NET SDK 10、Node 24（仅前端步骤需要）。

```bash
./build.sh        # Git Bash / Linux
build.cmd         # Windows CMD
```

两条命令等价：`dotnet build` + `dotnet test`，全绿即通过。

NuGet 源见 `nuget.config`（华为云镜像优先，官方源回退）。

## 关键实现决策（详见实现计划对话）

- 后端与内核：C# / .NET 10；适配器静态注册随平台构建（不做动态程序集加载）
- 前端：TypeScript + Vite + React，渲染器 SVG
- 存储：SQLite + 本地文件（磁带/关卡包/报告落文件），单进程、无账号
- 门2执行：子进程 + 文件交换（Unity batchmode），平台不装引擎
