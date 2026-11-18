using LevelForge.Kernel.Analysis;

namespace LevelForge.Adapters.Mahjong;

// 接入产物内容常量：视觉映射 / 机制表 / 玩法规则书 / 格式说明书 / 规则矩阵 / 难度权重 / 内置样例关
// （与 onboarding-output/ 下的文档同源；平台 GameRegistration 消费这些字符串）
public static class MahjongContent
{
    // 内置样例关：真实语料 1.json 全文（16 只棋子全预设，含一对爆竹棋——求解抽象按普通棋处理）
    public const string RealLevel1Json = """
    {
      "level": 1,
      "difficulty": 1,
      "isDailyChallenge": false,
      "chessCount": 16,
      "mapWidth": 6,
      "mapHeight": 8,
      "chesses": [
        {"id": 1, "x": 1, "y": 2, "type": 28, "specialType": 0 },
        {"id": 2, "x": 2, "y": 2, "type": 28, "specialType": 0 },
        {"id": 3, "x": 3, "y": 2, "type": 29, "specialType": 0 },
        {"id": 4, "x": 3, "y": 3, "type": 29, "specialType": 0 },
        {"id": 5, "x": 4, "y": 2, "type": 30, "specialType": 0 },
        {"id": 6, "x": 0, "y": 2, "type": 30, "specialType": 0 },
        {"id": 7, "x": 1, "y": 1, "type": 19, "specialType": 0 },
        {"id": 8, "x": 1, "y": 4, "type": 19, "specialType": 0 },
        {"id": 9, "x": 0, "y": 1, "type": 10, "specialType": 0 },
        {"id": 10, "x": 1, "y": 0, "type": 10, "specialType": 0 },
        {"id": 11, "x": 2, "y": 4, "type": 1, "specialType": 0 },
        {"id": 12, "x": 4, "y": 3, "type": 1, "specialType": 0 },
        {"id": 13, "x": 2, "y": 3, "type": 43, "specialType": 2 },
        {"id": 14, "x": 0, "y": 0, "type": 43, "specialType": 2 },
        {"id": 15, "x": 2, "y": 5, "type": 31, "specialType": 0 },
        {"id": 16, "x": 5, "y": 2, "type": 31, "specialType": 0 }
      ],
      "obstacles": []
    }
    """;

    // 门1 代表关：全预设小关（8×8，10 只棋子 + 1 墙体 + 1 空位 + 1 墙障碍段），
    // 覆盖 Normal/Ice/Cont/Flip/Wall 全部 specialType 与障碍墙——键判别维度对与确定性检查的样本源
    public const string Gate1SampleLevelJson = """
    {
      "level": 9001,
      "difficulty": 1,
      "isDailyChallenge": false,
      "chessCount": 12,
      "mapWidth": 8,
      "mapHeight": 8,
      "chesses": [
        {"id": 1, "x": 0, "y": 0, "type": 1, "specialType": 0},
        {"id": 2, "x": 1, "y": 0, "type": 1, "specialType": 0},
        {"id": 3, "x": 2, "y": 0, "type": 2, "specialType": 0},
        {"id": 4, "x": 3, "y": 0, "type": 2, "specialType": 0},
        {"id": 5, "x": 4, "y": 0, "type": 3, "specialType": 1, "specialInfo": 2},
        {"id": 6, "x": 5, "y": 0, "type": 3, "specialType": 1, "specialInfo": 2},
        {"id": 7, "x": 6, "y": 0, "type": 4, "specialType": 5, "specialInfo": 0},
        {"id": 8, "x": 7, "y": 0, "type": 4, "specialType": 5, "specialInfo": 1},
        {"id": 9, "x": 0, "y": 1, "type": 5, "specialType": 3, "specialInfo": 7},
        {"id": 10, "x": 1, "y": 1, "type": 5, "specialType": 3, "specialInfo": 7},
        {"id": 11, "x": 7, "y": 7, "type": 6, "specialType": 4},
        {"id": 20, "x": 0, "y": 2, "type": 0, "specialType": 0}
      ],
      "obstacles": [
        {"id": 1, "x": 2, "y": 2, "dir": 4, "specialType": 1}
      ]
    }
    """;

    public const string VisualMappingJson = """
    {
      "version": 1,
      "board": { "background": "#101418" },
      "cellTypes": {
        "grid": { "shape": "block", "fill": "#232a35", "size": 0.95 }
      },
      "entityTypes": {
        "chess_normal": { "shape": "circle", "fill": "#3f6fb5", "props": { "face": { "render": "badge" }, "frozen": { "render": "tint", "color": "#8fd0ff" } } },
        "chess_ice": { "shape": "rounded", "fill": "#6fb1c9", "props": { "face": { "render": "badge" }, "specialInfo": { "render": "badge" }, "frozen": { "render": "tint", "color": "#8fd0ff" } } },
        "chess_pop": { "shape": "circle", "fill": "#c9584f", "props": { "face": { "render": "badge" } } },
        "chess_cont": { "shape": "rounded", "fill": "#4fa07a", "props": { "face": { "render": "badge" }, "specialInfo": { "render": "badge" } } },
        "chess_flip": { "shape": "square", "fill": "#a07a4f", "props": { "face": { "render": "badge" }, "specialInfo": { "render": "badge" } } },
        "chess_wall": { "shape": "square", "fill": "#5a5f66" }
      },
      "globalFields": [
        { "path": "remaining", "label": "剩余可消棋子", "render": "text" },
        { "path": "pairs", "label": "剩余对数", "render": "text" },
        { "path": "levelId", "label": "关卡号", "render": "text" },
        { "path": "difficulty", "label": "难度", "render": "text" }
      ]
    }
    """;

    public const string MechanicsJson = """
    {
      "version": 1,
      "levels": [
        {
          "range": "1-999",
          "mechanics": [
            "adjacentMatch",
            "lineOfSightMatch",
            "dragPushChain",
            "iceChess",
            "contChess",
            "flipChess",
            "wallChess",
            "wallObstacle"
          ],
          "excluded": ["popCascade"],
          "note": "全预设求解语义 = 游戏 LevelSolvabilitySolver 官方抽象：POP/FLIP 按普通棋参与消除；爆竹随机连锁（popCascade）不在求解语义内，出题端不得依赖其随机收益设计关卡"
        }
      ]
    }
    """;

    public const string GameBrief = """
    # 麻将连连看（mahjong_260417）规则书

    棋盘为 W×H 网格（当前关卡 6×8 / 8×10 / 10×12），初始满盘（或按 chesses 显式给出，type=0 为永久空位）。
    玩家通过"点击消除"与"拖动推链"两种操作两两消除同牌面棋子，清空全部非墙棋子即胜利。

    ## 消除规则
    1. 相邻消除：两枚同 type 棋子处于四邻格（|dx|+|dy|=1）且中间无墙体 → 可消除。
    2. 无遮挡消除：两枚同 type 棋子在同一行或同一列、距离大于 1，且二者之间所有格无棋子无墙 → 可消除。
    3. 点击棋子后，系统高亮全部可消除候选（相邻 + 无遮挡），玩家点选其中一枚成对消除；
       只有一个候选时直接消除。

    ## 拖动与推动链
    1. 棋子可沿四方向拖动，最多拖到该方向"可移动格数"处（遇墙/边界/不可推棋子停止）。
    2. 拖动方向前方有连续相邻棋子时，整条推动链一起位移；连排组（CONT，同 specialInfo）整组刚性跟随。
    3. 松手时按落点构建虚拟棋盘判定消除：源棋在新位置的可消除候选存在 → 立即消除一对并写回落点；
       无候选 → 整链弹回原位（不存在"只移动不消除"的中间局面）。
    4. 松手候选多于一个时棋子停在落点，玩家再点选其中一枚消除。

    ## 特殊棋子
    - 冰冻（ICE，specialInfo=解冻计数）：未解冻不可拖动/消除/推动；场上每消除一批棋子（每批 2 枚）全体冰块计数 -2，归零自动解冻转为普通棋。
    - 爆竹（POP，type=43）：消除后发射爆竹炸掉随机一对同 type 棋子（随机收益，不进求解语义——出题请勿依赖）。
    - 连排（CONT，specialInfo=组号）：同组棋子为一个整体，任一枚被拖动时整组同位移；被推动时整组入链。
    - 墙体棋（WALL）：不可拖动/消除/推动，不计入胜利条件（清盘 = 非墙棋子清空）。
    - 翻面（FLIP，specialInfo 0 背面/1 正面）：运行时背面不可消除、每消除一对全体翻转、对数锁定正面——
      求解抽象（游戏 LevelSolvabilitySolver 官方口径）将翻面按普通棋处理。

    ## 失败条件
    消除后剩余非墙棋子 < 2，或场上不存在任何可消除走法 → 判负（求解器中表现为无合法走法的死局分支）。
    倒计时归零判负属于时间压力，不影响离散消除规则，不在求解语义内。

    ## 出题约束
    - 只输出全预设（flattened）关卡：chesses 数量必须等于 chessCount，每个格子显式给出（type=0 表示空位）。
    - fillTypeCount/adjacentPairCount/popChessPairCount 是游戏运行时填充参数，flatten 后应为 0 或缺省。
    - 每种牌面的棋子数应为偶数（奇数必有奇偶不变量 → 无解）。
    - 牌面取值 1~42（麻将牌面）；43 为爆竹专属牌面，与 specialType=2（爆竹）配套使用。
    """;

    public const string FormatSpec = """
    # 关卡格式说明书（mahjong_260417 全预设形态）

    输出一个 JSON 对象（UTF-8，无注释无尾逗号），字段：

    | 字段 | 类型 | 约束 |
    |---|---|---|
    | level | 整数 | ≥1，关卡编号 |
    | difficulty | 整数 | ≥0（0~3 档） |
    | isDailyChallenge | 布尔 | 是否每日挑战关 |
    | chessCount | 整数 | = chesses 数组长度（全预设硬性要求） |
    | mapWidth / mapHeight | 整数 | 2~32；棋盘列数/行数 |
    | chesses | 数组 | 棋子对象；type=0 表示该格为空位 |
    | obstacles | 数组 | 墙障碍对象（无墙时给 []） |

    chesses 元素：{"id":正整数唯一,"x":0..mapWidth-1,"y":0..mapHeight-1,"type":牌面(≥0),
    "specialType":0普通/1冰冻/2爆竹/3连排/4墙体/5翻面,"specialInfo":整数（普通=0 或缺省；
    冰冻=解冻计数；连排=组号(同组一致)；翻面=0背面/1正面；爆竹=0）}

    obstacles 元素：{"id":整数,"x":依附格列,"y":依附格行,"dir":1上/2下/3左/4右,"specialType":1墙}
    （dir 指墙在该格的哪条夹缝上；相邻两格分别在各自朝向声明等价）

    硬性要求：
    - 每格至多一枚棋子；id 全局唯一；
    - 非墙棋子总数必须为偶数（成对消除，奇数必无解）；
    - specialType=2（爆竹）的棋子 type 必须为 43；
    - fillTypeCount/adjacentPairCount/popChessPairCount 不得出现（生成期参数，flatten 后无意义）。
    """;

    public const string RuleMatrix = """
    # 规则矩阵 · mahjong-260417（开发者确认件）

    | # | 规则 | 语义 | 证据（游戏源码） | 状态键维度 | 测试 | 对拍样本 |
    |---|---|---|---|---|---|---|
    | 1 | 相邻消除 | 同 type 四邻 + 无墙 → 配对消除 | ChessQueryHelper.canMatchChess:64-81 | 棋子位置/牌面 | 相邻消除用例 | 全预设关 A |
    | 2 | 无遮挡消除 | 同轴、距离>1、中间全空无墙 | canMatchLineOfSight:113-152 | 棋子位置/牌面/空格 | 无遮挡用例 | 全预设关 A |
    | 3 | 拖动推动链 | 方向最大格数 = min(自身链尾起算, 连排组末端限制)；整链同位移 | ConnectedChessController.calcMaxEmptySlotsInDir:651-657 | 棋子位置 | 推动链用例 | 全预设关 A |
    | 4 | 连排组 | 同 specialInfo 的 CONT 整组刚性同位移 | Chess.ts getConnectedGroup:72-82 | 连排组号 | 连排用例 | 全预设关 A |
    | 5 | 冰冻 | specialInfo>0 冻结；每批消除全体 -2；≤0 转普通 | IceChessController.onChessEliminated:77-93 | 冰冻计数 | 解冻用例 | 全预设关 A |
    | 6 | 翻面（抽象） | 求解按普通棋；朝向/全局翻转/锁定不在语义内 | LevelSolvabilitySolver 头注:47-55 | ——（归并不入键） | 抽象口径用例 | 对拍须同抽象口径 |
    | 7 | 爆竹（抽象） | 求解按普通棋；随机连锁不在语义内 | LevelSolvabilitySolver 头注:49；PopChessController.pickRandomPair:337 | ——（归并不入键） | 抽象口径用例 | 对拍须同抽象口径 |
    | 8 | 墙体棋 | 不可拖/消/推；不计入胜利 | WallChessController:9-47 | 墙体棋位置 | 墙体用例 | 全预设关 A |
    | 9 | 墙障碍 | 格间夹缝墙，两侧声明等价；阻断相邻/无遮挡/推动 | ObstacleManager.hasWallBetween:72-92 | 墙体配置 | 墙阻断用例 | 全预设关 A |
    | 10 | 胜利 | 非墙棋子清空 | GameManager.evaluateOutcomeAfterEliminate:1089-1095 | ——（判定） | 清盘用例 | 全预设关 A |
    | 11 | 死局判负 | 剩余<2 或无任何可消除走法 → 失败 | GameManager.checkDeadlockAfterEliminate:1102-1117 | ——（搜索死分支） | 死局用例 | —— |
    | 12 | 求解对象 | 全预设 flattened config（chesses.length == chessCount）；生成型规格子集外拒绝 | LevelLayoutBuilder.flattenConfig:277-300、initFromPreloaded:194-229 | —— | 子集外拒绝用例 | —— |

    ## 状态维度 ↔ 键覆盖对照
    | 维度 | 入键字段 |
    |---|---|
    | 棋子 id / 位置 / 牌面 | id:x,y,type |
    | specialType（普通/冰/连排/墙；爆竹与翻面归并为普通） | special |
    | 冰冻计数 / 连排组号 | info |
    | 墙障碍配置 | w=x,y,dir |
    | 棋盘尺寸 | 前缀 WxH |
    | （翻面朝向、爆竹连锁、倒计时、道具、每日挑战） | 不在求解语义内（见 #6/#7 与资格报告边界） |
    """;

    public static readonly DifficultyWeights DefaultDifficultyWeights =
        new("mahjong-260417", "mahjong-fixed-v1", 1.0, 0.2, [20, 44]);
}
