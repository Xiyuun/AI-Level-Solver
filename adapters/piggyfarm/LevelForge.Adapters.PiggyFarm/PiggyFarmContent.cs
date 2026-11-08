using System.Text.Json;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Replay;

namespace LevelForge.Adapters.PiggyFarm;

// piggyfarm 接入产物：原生关卡样例 + 视觉映射 / 机制表 / 玩法书 / 格式说明书 / 规则矩阵 / 难度权重
// 全部随插件程序集发布，并由 GameRegistration 交给平台（向导③验收与工作台渲染直接消费）
public static class PiggyFarmContent
{
    public const string AdapterVersionValue = "1.0.0";

    // 确定性/门1 使用的代表关：原生编辑源 000001.json 原样嵌入（6 只普通猪，BFS 可解，30 步内）
    // 不作为内置关卡入库（导入玩法没有内置关；BuiltinLevels 返回空），只用于门1 样本与自测
    public const string SampleLevelJson = """
    {
      "levelId": 1, "difficulty": 1, "levelName": "nlevel 1",
      "mapWidth": 12, "mapHeight": 19, "pigCount": 6,
      "pigs": [
        { "id": 0, "facing": "Left",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 3, "y": 2 }, { "x": 4, "y": 2 }] },
        { "id": 1, "facing": "Down",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 5, "y": 3 }, { "x": 5, "y": 2 }] },
        { "id": 2, "facing": "Down",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 5, "y": 5 }, { "x": 5, "y": 4 }] },
        { "id": 3, "facing": "Down",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 4, "y": 4 }, { "x": 4, "y": 3 }] },
        { "id": 4, "facing": "Down",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 3, "y": 4 }, { "x": 3, "y": 3 }] },
        { "id": 5, "facing": "Left",  "pigType": "Normal", "specialInfo": "", "occupiedCells": [{ "x": 3, "y": 5 }, { "x": 4, "y": 5 }] }
      ],
      "terrainElements": [],
      "specialData": { "rabbit": { "firstAppearStep": 0, "subsequentAppearStep": 0, "rabbitCountPerWave": 0 } }
    }
    """;

    // 视觉映射（渲染规范 §3）：头格圆角 + 朝向箭头，身体格同色圆角；空棋盘背景 + 网格
    public const string VisualMapping = """
    {
      "version": 1,
      "board": { "background": "surface", "grid": true },
      "cellTypes": {},
      "entityTypes": {
        "pigHeadNormal": {
          "shape": "rounded", "fill": "#e58fb1", "size": 0.92,
          "props": { "facing": { "render": "arrow", "color": "#ffffff",
            "rotate": { "Up": 0, "Right": 90, "Down": 180, "Left": 270 } } }
        },
        "pigBodyNormal": { "shape": "rounded", "fill": "#d97aa0", "size": 0.86 },
        "pigHeadLong": {
          "shape": "rounded", "fill": "#5b8bd6", "size": 0.92,
          "props": { "facing": { "render": "arrow", "color": "#ffffff",
              "rotate": { "Up": 0, "Right": 90, "Down": 180, "Left": 270 } },
            "pigType": { "render": "badge", "text": "长", "color": "#1f3f7a" } }
        },
        "pigBodyLong": { "shape": "rounded", "fill": "#4a78bd", "size": 0.86 },
        "pigHeadRichPig": {
          "shape": "rounded", "fill": "#e0a72e", "size": 0.92,
          "props": { "facing": { "render": "arrow", "color": "#ffffff",
              "rotate": { "Up": 0, "Right": 90, "Down": 180, "Left": 270 } },
            "pigType": { "render": "badge", "text": "富", "color": "#5b3a00" } }
        },
        "pigBodyRichPig": { "shape": "rounded", "fill": "#c98f1f", "size": 0.9 },
        "pigHeadBoom": {
          "shape": "rounded", "fill": "#d94b4b", "size": 0.92,
          "props": { "facing": { "render": "arrow", "color": "#ffffff",
              "rotate": { "Up": 0, "Right": 90, "Down": 180, "Left": 270 } },
            "pigType": { "render": "badge", "text": "爆", "color": "#651b1b" } }
        },
        "pigBodyBoom": { "shape": "rounded", "fill": "#bf3b3b", "size": 0.86 },
        "pigHead": {
          "shape": "rounded", "fill": "#e58fb1", "size": 0.92,
          "props": { "facing": { "render": "arrow", "color": "#ffffff",
            "rotate": { "Up": 0, "Right": 90, "Down": 180, "Left": 270 } } }
        },
        "pigBody": { "shape": "rounded", "fill": "#d97aa0", "size": 0.86 }
      },
      "globalFields": [
        { "path": "remainingPigs", "label": "剩余猪", "render": "text" }
      ]
    }
    """;

    // 机制解锁表：v1 只实现"点击滑动"；特殊猪/地形/兔子等机制不在子集内（导入时跳过并注明）
    public const string MechanicsTable = """
    {
      "version": 1,
      "ranges": [
        { "from": 1, "to": 499, "mechanics": ["click-slide", "edge-exit"],
          "note": "v1 规则子集：普通猪/大长猪/土豪猪（滑动），爆炸猪（碰撞扣爆炸计数，归零判负）。其余特殊猪、升降梯、动态兔子在关卡校验阶段按子集外拒绝" }
      ]
    }
    """;

    // 玩法规则书（生成端提示词的规则书；v1 未接 ILevelComposer，生成端暂不可用——如实注明）
    public const string GameBrief = """
    # 玩法：PiggyFarm · ClassicMode（点击滑动解谜）

    12×19 网格（编辑源全部关卡均为 12 列 × 19 行）。场上每只猪占据若干格并有一个固定朝向。
    玩家点击一只猪，它沿自身朝向直线滑动；撞到其它猪或地图边界才停。

    ## 规则
    - 点击的猪只有「前进一步不被其它猪的占格挡住」才能起步；
    - 起步后沿朝向连续滑动：前方被其它猪占格挡住则停下（停在其前一格）；
    - 有任意一格越过地图边界时，整只猪沿场外跑道离场（从棋盘移除）；
    - 滑动只沿朝向，朝向不会改变；猪之间不会互相推动；
    - 胜利：场上猪全部离场（清空）；
    - 死局：场上仍有猪但没有任何一只可以起步（游戏侧会进入失败/复活流程）；
    - 爆炸猪：脚印同普通猪；与其它猪发生碰撞（自己撞上去或被撞）时爆炸计数 -1；
      specialInfo 是正整数剩余次数（缺省 3），归零立即判负。一次滑动只结算一次碰撞。
    - v1 不建模：弹簧/鸭子/铁笼/锁/链接/剪刀/睡觉/气球/农场主/丧尸/背包松鼠/定时爆炸猪、
      升降梯地形、动态兔子、道具（消除/洗牌/翻转/撤回）。

    ## 出题硬约束（生成候选必须满足；平台会机检，不合格直接重生）
    - 至少 3 种朝向；禁止所有猪同朝向、禁止单排直道/规则等距网格；
    - 初始可直接起步的猪 ≤ 总数的 50%；至少 20% 的猪被其它猪阻挡（要有解谜依赖）；
    - 猪之间必须形成跨行/跨列的阻挡关系，不能是 7 条互不相干的直道；
    - v1 支持 Normal / Long / RichPig / Boom（前三种滑动规则相同、脚印不同；Boom 有碰撞计数）、无地形；
    - 1 档：000001 的 6 猪互锁模块（1~3 个，6~18 只）；
    - 2 档：项目原生 difficulty=2 且可解的 374（77 只，92 步）；
    - 3 档：项目原生 difficulty=3 的 19/29/59（79~96 只，121~147 步）+ 112 只密集模板；
    - 难度阈值 v4：<20 为 1 档、20~100 为 2 档、>=100 为 3 档（与项目原生 difficulty 对齐）。
    - 生成时按种子做对称变换/重编号；关卡设计来自项目真实内容，保证玩家挑战性。
    """;

    // 格式说明书：项目原生编辑源 JSON（Assets/Main/LevelEditor/Luban~/Datas/Levels/*.json）直通
    public const string FormatSpec = """
    # 关卡格式说明书 · PiggyFarm ClassicMode（原生编辑源）

    平台全程直用项目原生格式：`Assets/Main/LevelEditor/Luban~/Datas/Levels/000001.json …`（499 个）。
    适配层不做 IO，只解析平台读到的 JSON 原文。

    ## 字段
    | 字段 | 含义 |
    |---|---|
    | levelId | 关卡编号，与六位文件名一致（000001.json → 1） |
    | difficulty | 1 普通 / 2 困难 / 3 极难 |
    | levelName | 关卡显示名 |
    | mapWidth / mapHeight | 地图格数（v1 限 4~64；项目内为 12×19） |
    | pigCount | 地面猪数量，必须等于 pigs 长度（不含升降梯下层猪） |
    | pigs[] | 地面猪：id / facing(Up/Down/Left/Right) / pigType / specialInfo / occupiedCells[] |
    | pigs[].occupiedCells[] | 该猪初始占据的格子（x 列、y 行，原点左上、y 向下增大），长条猪/土豪猪是多格 |
    | terrainElements[] | 升降梯矩形列表（v1 不支持，非空即按子集外拒绝） |
    | specialData.rabbit | 动态兔子配置（v1 仅接受全零） |
    | specialData.pigSkin | 皮肤（纯表现，不影响规则；v1 忽略） |

    ## 静态校验规则（v1）
    1. JSON 根为对象；levelId 为正整数；difficulty ∈ {1,2,3}；mapWidth/mapHeight ∈ [4,64]；
    2. pigCount 必须等于 pigs 数量；pig id 唯一非负；facing 四值之一；
    3. pigType 支持 Normal / Long / RichPig / Boom；
       Boom 的 specialInfo 必须是正整数剩余爆炸次数（空=3），脚印与 Normal 相同（沿朝向 2 格）；
    4. occupiedCells 非空、界内、无重复，且构成实心矩形；
       脚印尺寸与类型/朝向匹配：Normal 沿朝向 2 格、Long 3 格、RichPig 2×3（宽 2 高 3，按朝向旋转）；
    5. 地面猪之间不得占用同一格；
    6. terrainElements 非空 / specialData.rabbit 非零 → 报「v1 支持子集之外」，导入时跳过并注明原因。

    ## 子集外判定与导入口径
    错误信息以 `v1 支持子集之外：` 开头时，平台导入器把它计入「跳过」而不是「失败」，
    并在导入明细里保留原因。v1 在 499 个编辑源中支持 96 关（Normal/Long/RichPig/Boom、无地形、无兔子）。
    """;

    // 规则矩阵（AI 初稿，待开发者逐行确认；门1 的维度样本与单元测试逐条对应）
    public const string RuleMatrix = """
    # 规则矩阵 · PiggyFarm ClassicMode（v1 子集）

    | 规则/机制 | 适用条件 | 预期 | 状态维度 | 单元测试 | 确认 |
    |---|---|---|---|---|---|
    | 点击起步 | 前进一步整块脚印不与其它猪占格相交 | 允许起步；否则不可点击 | 猪占格 | Test: 阻挡_贴脸不可起步 | AI 初稿待确认 |
    | 滑动停步 | 滑行中前方脚印格被其它猪占用 | 停在阻挡前一格，不推动对方 | 双方占格 | Test: 滑行_被挡停在前一格 | AI 初稿待确认 |
    | 边界离场 | 脚印有任一格越出地图 | 整只猪离场，从棋盘移除 | 在场集合 | Test: 滑行_越界离场 | AI 初稿待确认 |
    | 多格脚印 | Normal 1×2、Long 1×3、RichPig 2×3（随朝向旋转） | 整块脚印一起滑动/判定阻挡 | 占格集合 | Test: 解析_脚印尺寸匹配类型 | AI 初稿待确认 |
    | 朝向不变 | 任何点击 | 滑动不改变朝向，只沿朝向移动 | 朝向 | Test: 状态键_朝向不同键不同 | AI 初稿待确认 |
    | 胜利判定 | 场上猪全部离场 | IsGoal=true | 在场集合 | Test: 胜利_最后一只离场 | AI 初稿待确认 |
    | 死局语义 | 仍有猪且无合法动作 | 无解或资源未知由求解结论表达，不冒充 | — | Test: 已知死局_穷尽可证无解 | AI 初稿待确认 |
    | 皮肤 | specialData.pigSkin | 纯表现，不影响规则 | 不进入状态 | Test: 解析_皮肤关仍可导入 | AI 初稿待确认 |
    | 爆炸计数 | Boom 与其它猪碰撞（含被撞） | 移动方/被撞方各扣 1；归零判负 | 剩余次数 | Test: Boom_被撞三次后状态失败 | AI 初稿待确认 |
    | 子集外机制 | 其它特殊猪/升降梯/兔子 | 校验阶段拒绝导入并注明原因 | — | Test: 校验_子集外按跳过口径 | AI 初稿待确认 |
    | 状态维度总表 | 地图尺寸 + 每猪(Id/类型/朝向/占格) | StateKey 覆盖全部维度 | — | Test: 状态键_猪顺序无关 | AI 初稿待确认 |

    备注（诚实边界）：
    - 本适配层是 v1 子集：499 关编辑源中支持 96 关（含 Boom 关），其余 403 关因其它特殊猪/地形/兔子被跳过；
    - 门2 真机对拍尚未接入（项目内有 Unity harness 的既往改造记录，需登记驱动后另跑）；
    - 求解器不是内核 BFS，而是本适配层的两阶段搜索；结论按内核五维度如实表达（预算耗尽≠无解）。
    """;

    // 动作 schema：结构化动作只含猪号（无歧义定位目标；禁止从 DescribeMove 文本反解析）
    public const string MoveSchema = """
    {
      "version": 1,
      "action": "click-pig",
      "fields": {
        "pigId": { "type": "integer", "required": true, "description": "场上猪的关卡内 Id（pigs[].id）" }
      },
      "note": "一次动作 = 点击一只猪并让其沿朝向滑行到阻挡/离场"
    }
    """;

    // 语义投影 schema：对拍比较的唯一来源（覆盖规则矩阵声明的全部状态维度）
    public const string SemanticSchema = """
    {
      "version": 1,
      "fields": {
        "mapWidth": "int（地图列数）",
        "mapHeight": "int（地图行数）",
        "pigs": [
          { "id": "int", "type": "string", "facing": "Up|Down|Left|Right", "cells": [{ "x": "int", "y": "int" }],
            "counter": "int（仅 Boom：剩余爆炸次数）" }
        ]
      },
      "normalization": "pigs 按 id 升序；cells 按 (y,x) 升序；位置/朝向/类型/在场集合全部进入投影"
    }
    """;

    // 难度权重（MVP 固定权重形态）：解长为主、平均分支为辅；阈值按 499 关猪数分布粗定
    public static readonly DifficultyWeights Weights = new(
        "piggyfarm", "piggyfarm-fixed-v4",
        SolutionLengthWeight: 1.0,
        BranchingWeight: 0.2,
        // 与项目原生 difficulty 对齐：原生 2 档可解关（374，92 步）→ 2 档；
        // 原生 3 档（19/29/59，121~147 步）→ 3 档。1 档 <20，2 档 20~100，3 档 >=100。
        Thresholds: [20.0, 100.0]);

    // 权重 JSON：GameRegistration 需要字符串形态（camelCase + 枚举小写口径与平台一致）
    public static readonly string DifficultyWeightsJson =
        JsonSerializer.Serialize(Weights, TapeJson.Options);
}
