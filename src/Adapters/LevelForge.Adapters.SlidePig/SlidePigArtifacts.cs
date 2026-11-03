using LevelForge.Kernel.Analysis;

namespace LevelForge.Adapters.SlidePig;

// 滑动猪 · 接入产物资产：规则矩阵/视觉映射/机制表/玩法文档/格式说明书（内容依据 PiggyFarm 项目源码与模块文档）
public static class SlidePigArtifacts
{
    #region 规则矩阵（v1 支持子集；证据 = PiggyFarm 源码/文档位置）

    // 每行一条规则：语义 + 证据出处；④人工审核时逐行核对，争议语义必须转用例
    public const string RuleMatrix = """
    # 规则矩阵 · 滑动猪 v1（核心子集，覆盖 110 关中的 75 关）

    > 开发者审核（④人工审核）：**2026-11-03 全部确认通过**（15 条规则 + 5 条模型口径无异议，开发者邓翔）。
    > 下表"待开发者确认"字样为撰写时点的历史记录，审核结论以本声明为准。

    来源项目：PiggyFarm（Unity 2022.3.62f3c1），关卡 = Luban Levels JSON（000001~000110）。
    v1 范围决定：数据扫描显示 110 关仅使用 Normal/Long/SmallElephant/Boom/Spring/Lock/Cage/CageL/Duck/Rope/Scissors，
    且无 terrainElements/specialData（升降梯与兔子未用于现有关卡）。
    v1 支持 Normal/Boom/Spring/Long/Lock/SmallElephant/Cage/CageL（75 关）；
    Duck（26 关）与 Rope/Scissors（9 关）确定性可建模，留 v2；其余类型现有关卡未使用。

    | 规则 | 语义 | 证据（PiggyFarm 内） | 确认 |
    |---|---|---|---|
    | 点击滑行 | 点击猪→沿朝向整块逐格前移，直到被挡或离场 | ClassicMode/Docs/GAMEPLAY.md 一句话玩法；BoardDeadlockDetector 的"整块前移一格"同口径 | 待开发者确认 |
    | 离场 | 前缘目标格全部越界=触发 EndWall，整猪离场移除 | NormalPig.TryStartExitAfterMove（EndWall 触发）；GAMEPLAY.md"撞到边界离场回收" | 待开发者确认 |
    | 阻挡停止 | 前缘目标格被其他猪脚印占用→停在贴靠格（前缘相邻） | GAMEPLAY.md 死局判定节"占用表命中任一格即被挡" | 待开发者确认 |
    | 胜利 | 未失败且场上无猪（含铁笼：归零自删后不在场） | GameManager.TrySettleIfCleared 遍历 SpawnedPigs + CountsTowardLevelClear（仅 Farmer=false，子集内无 Farmer） | 待开发者确认 |
    | 爆炸猪计数 | 碰撞双方各自扣自身计数（撞人/被撞都扣）；归零=本局失败终态 | BoomPig.OnSelfPigHitOther / OnSelfPigHitByOther / ConsumeBoomCount | 待开发者确认 |
    | 爆炸计数解析 | specialInfo 正整数，无效默认 3 | BoomPig.ResolveBoomCount + initialBoomCount=3 | 待开发者确认 |
    | 锁猪 | specialInfo 非负整数（0=已解锁），无效默认 10；>0 时点击不起步 | LockPig.SkipWalkAfterClick / ResolveUnlockCount + initialUnlockCount=10 | 待开发者确认 |
    | 他猪离场扣计数 | 每次他猪离场：所有在场 Lock/Cage 计数>0 者各扣 1 | PigManager.NotifyPigExited → FanOutPeers → OnPeerPigExited（LockPig/Cage 同实现） | 待开发者确认 |
    | 铁笼自删 | Cage/CageL 不可移动纯阻挡；计数归零自删且**不触发离场事件**（无级联扣其他锁） | Cage.CoPlayUnlockThenRecycle（无 NotifyPigExited 调用） | 待开发者确认 |
    | 铁笼叠放与关猪 | 铁笼脚印可与猪叠放（笼内猪）；被关住的猪不可起步，笼删自动解锁 | Pig.cs Cage 注释"可与猪同格叠放"；NormalPig.SetMovementLocked（铁笼关住时为 true） | 待开发者确认 |
    | 铁笼初始 0 | specialInfo=0 的铁笼在初始化即自删 | Cage.Init 分支 _remainUnlockCount<=0 | 待开发者确认 |
    | 弹簧猪 | 滑行撞猪后原路弹回原位（净位置不变，碰撞回调照常）；无阻挡则正常离场 | SpringPig.BeginRebound / ReboundSpeedFactor=2 / _ignorePig | 待开发者确认 |
    | 朝向不变 | 全程朝向不变（翻转属道具，v1 无道具） | GAMEPLAY.md 道具节（Flip/Shuffle 为玩家道具） | 待开发者确认 |
    | 失败终态 | 爆炸计数归零=失败，无后继（游戏为复活流程，v1 无道具无复活） | GAMEPLAY.md 当前所有失败来源①③ | 待开发者确认 |
    | 多格脚印 | Long=1×3、SmallElephant=2×3 由 occupiedCells 直接给出，移动整块平移 | LevelPigRecord.OccupiedCells；GAMEPLAY.md 猪种类表 | 待开发者确认 |

    ## 模型口径决定（REVIEW 项，审核重点）
    1. **顺序点击模型**：一次点击完全结算后再下一击。真实游戏允许连点飞行连锁（GAMEPLAY 碰撞受击节），
       属超集；求解器只在顺序策略空间内搜索。对拍按"逐步注入+等待落定"执行，与模型一致。
    2. **被挡点击排除**：前缘被挡的点击在游戏里为原地反馈（弹簧猪为撞-弹回，净效果仅爆炸扣减——对本模型
       恒为无收益或有害），故不列入合法操作。REVIEW：请确认不存在"被挡点击有正面收益"的机制。
    3. **网格贴靠与物理停格回退（2026-11-03 门2实证修订）**：物理层的"贴靠格被第三猪占用时沿来向
       寻找安全停格"（PigManager.TryFindFreeStopFootprint，最多回退 32 格）原以为网格模型按构造排除——
       **已被门2实证证伪**：64 号关第 46 步，游戏日志"猪 69 的贴靠格被占，向后调整 4 格"，稠密堆积局
       物理停格与占用表粒度不一致时该兜底真实触发，网格模型无法忠实复现（触发依赖物理接触细节）。
       v1 处置：作为模型边界落档，64 号关对拍红灯=已知边界（非未知错误）；v2 再评估建模可行性。
       附带时序口径：铁笼计数归零后游戏播解锁动画再回收，harness 落定等待含"铁笼回收完毕"（动画属
       本次点击结算的一部分）；适配层在计数归零瞬间移除，两者同口径。
    4. **碰撞同帧等价**：游戏物理帧末按猪 ID 排序结算同帧碰撞；网格模型一次点击只产生一次碰撞，无排序歧义。
    5. **资源条件声明：无道具通关模型**（开发者确认 2026-11-02）：所有关卡设计上保证不使用任何道具可通关
       （公司全线游戏一致，无逼迫用道具的设定）。因此模型结论=无道具可解性；稠密关求解"未知"为求解器
       搜索不足，非关卡设计问题——此确认为稠密关搜索改进的依据。
    """;

    #endregion

    #region 视觉映射 / 机制表 / 文档

    // 视觉映射：多格猪以锚点格渲染（v1 渲染器单格实体），area 属性提示面积
    public const string VisualMapping = """
    {
      "version": 1,
      "board": { "background": "surface", "grid": true },
      "cellTypes": {},
      "entityTypes": {
        "normal":    { "shape": "circle",  "fill": "#ee8fa0", "size": 0.85, "props": { "facing": { "render": "arrow", "color": "#ffffff" } } },
        "boom":      { "shape": "circle",  "fill": "#d9534f", "size": 0.85, "props": { "counter": { "render": "badge", "color": "#ffffff" } } },
        "spring":    { "shape": "circle",  "fill": "#5cb85c", "size": 0.85, "props": { "facing": { "render": "arrow", "color": "#ffffff" } } },
        "long":      { "shape": "rounded", "fill": "#f0ad4e", "size": 0.9 },
        "elephant":  { "shape": "rounded", "fill": "#8e7cc3", "size": 0.9 },
        "lock":      { "shape": "square",  "fill": "#a1887f", "size": 0.9, "props": { "counter": { "render": "badge", "color": "#ffffff" } } },
        "cage":      { "shape": "block",   "fill": "#78909c", "size": 0.95, "props": { "counter": { "render": "badge", "color": "#ffffff" } } },
        "cageL":     { "shape": "block",   "fill": "#78909c", "size": 0.95, "props": { "counter": { "render": "badge", "color": "#ffffff" } } }
      },
      "globalFields": [
        { "path": "pigsRemaining", "label": "剩余猪数", "render": "text" },
        { "path": "failed", "label": "失败态", "render": "text" }
      ]
    }
    """;

    // 机制解锁表：v1 全编号同机制集（按类型分布可后续细分；Boom 首现 71 关、Lock 31、Cage 57、Spring 91）
    public const string MechanicsTable = """
    {
      "version": 1,
      "ranges": [
        { "from": 1, "to": 110, "mechanics": ["Normal", "Long", "SmallElephant", "Boom", "Spring", "Lock", "Cage", "CageL"], "note": "v1 支持子集；Duck/Rope/Scissors 关卡在 v1 中标记不支持" }
      ]
    }
    """;

    // 玩法规则描述（生成端提示词的规则书）
    public const string GameBrief = """
    # 玩法：滑动猪（PiggyFarm ClassicMode）

    12×19 网格棋盘上有若干只猪，点击一只猪，它沿自身朝向直线滑动：撞到其他猪停在贴靠格，
    滑到边界则整只离场。清空全场即过关。

    ## 规则
    - 每只猪占 1~6 格（普通猪 1×2、长猪 1×3、小象 2×3），脚印由 occupiedCells 给出；
    - 点击 = 沿朝向整块逐格前移；前缘被占即停，前缘出界即整猪离场；
    - 朝向全程不变；
    - 爆炸猪（Boom）：被撞或撞猪都扣自己头顶计数，归零=本局失败；
    - 锁猪（Lock）：其他猪每离场一次扣 1 解锁数，>0 时点击不起步；
    - 铁笼（Cage/CageL）：不能移动纯挡路；其他猪离场扣次数，归零自删；
    - 弹簧猪（Spring）：撞猪后原路弹回原位（位置不变）；无阻挡则正常离场；
    - 胜利：场上没有任何猪和铁笼。

    ## 出题要点
    - 所有猪初始不得重叠、脚印须在棋盘内；
    - 至少留一条可行清场路线（求解器会验证）；
    - 难度来自阻挡关系与清场顺序规划：难度 1 少猪直出、难度 2 需绕序、难度 3 多猪多约束。
    """;

    // 格式说明书（生成端提示词的格式描述）
    public const string FormatSpec = """
    # 关卡格式说明书 · 滑动猪（Luban Levels JSON 原生格式）

    ```json
    {
      "levelId": 1, "levelName": "nlevel 1",
      "mapWidth": 12, "mapHeight": 19, "pigCount": 6,
      "pigs": [
        { "id": 0, "facing": "Down", "pigType": "Normal", "specialInfo": "",
          "occupiedCells": [ { "x": 2, "y": 16 }, { "x": 2, "y": 15 } ] }
      ]
    }
    ```

    - `facing` ∈ Up/Left/Down/Right（Up = y 减小方向）；
    - `pigType` v1 仅支持 Normal/Boom/Spring/Long/Lock/SmallElephant/Cage/CageL；
    - `specialInfo`：Boom=正整数爆炸计数（默认3）；Lock/Cage=非负整数解锁数（默认10）；其余为空串；
    - `occupiedCells`：猪的脚印格列表（普通猪 2 格、长猪 3 格、小象 6 格），不得重叠、不得越界；
    - `pigCount` 必须等于 pigs 数组长度。

    ## 静态校验规则（校验器逐条检查）
    1. 必填字段齐全，mapWidth/Height 在 1~50；
    2. 类型/朝向在枚举内且属 v1 支持子集；猪 id 唯一；
    3. 脚印非空、不越界、猪与猪不重叠；计数字段可解析；
    4. pigCount == pigs.length。
    """;

    // 默认难度权重（MVP 固定权重；阈值按求解实测后可手工对齐——待 75 关全量求解数据出来后调整）
    public static readonly DifficultyWeights DefaultDifficultyWeights = new(
        "slidepig", "slidepig-fixed-v1",
        SolutionLengthWeight: 0.5,
        BranchingWeight: 0.5,
        Thresholds: [6.0, 14.0]);

    #endregion
}
