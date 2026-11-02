using System.Text.Json.Nodes;

namespace LevelForge.Adapters.SlidePig;

// 滑动猪 · 类型与关卡/状态模型（内部参考实现；数值与游戏 PigType/LevelFacing 对齐，来源 PiggyFarm 项目）
// v1 支持子集（资格评估结论）：Normal/Boom/Spring/Long/Lock/SmallElephant/Cage/CageL，共 75/110 关；
// Duck(26关)/Rope/Scissors(9关) 与地形层/兔子/农场主等不在 v1——LoadLevel 对子集外类型明确拒绝

// 猪类型（取游戏运行时 PigType 数值；仅列 v1 支持子集）
public enum SlidePigType
{
    Normal = 0,            // 普通猪 1×2
    Boom = 1,              // 爆炸猪：碰撞扣计数，归零判负
    Spring = 2,            // 弹簧猪：撞猪后弹回原位（净效果仅碰撞回调）
    Long = 4,              // 长猪 1×3，逻辑同普通猪
    Lock = 5,              // 锁猪：他猪每次离场扣解锁数，>0 时不可起步
    SmallElephant = 6,     // 小象 2×3，逻辑同普通猪
    Cage = 13,             // 铁笼：不可移动纯阻挡；他猪离场扣次数，归零自删（不触发离场事件）
    CageL = 14             // L 形铁笼：同 Cage，脚印形状不同
}

// 朝向（与关卡 JSON facing 字符串一致；网格 y=0 在上）
public enum SlideFacing
{
    Up = 0,                // y-1
    Left = 1,              // x-1
    Down = 2,              // y+1
    Right = 3              // x+1
}

// 原生关卡里的一只猪（JSON 直读，未解析语义）
public sealed record SlidePigLevelPig(
    int Id,                                            // 关卡内唯一标识
    string Facing,                                     // Up/Left/Down/Right 原文
    string PigType,                                    // 类型名原文
    string SpecialInfo,                                // 计数等附加数据原文（Boom=爆炸数>0，Lock/Cage=解锁数>=0）
    (int X, int Y)[] OccupiedCells);                   // 脚印格（原顺序）

// 原生关卡（项目格式直通，无转换层）
public sealed record SlidePigLevel(
    int LevelId,                                       // 关卡号
    string LevelName,                                  // 关卡名
    int MapWidth,                                      // 棋盘列数
    int MapHeight,                                     // 棋盘行数
    IReadOnlyList<SlidePigLevelPig> Pigs)              // 猪列表
{
    // 从关卡 JSON 原文解析（字段缺失/类型错误抛 JsonException 或 FormatException）
    public static SlidePigLevel FromJson(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("关卡不是 JSON 对象");
        var pigs = new List<SlidePigLevelPig>();
        foreach (var p in (root["pigs"] as JsonArray)!.Cast<JsonObject>())
        {
            var cells = (p["occupiedCells"] as JsonArray)!
                .Select(c => (c!["x"]!.GetValue<int>(), c!["y"]!.GetValue<int>())).ToArray();
            pigs.Add(new SlidePigLevelPig(
                p["id"]!.GetValue<int>(),
                p["facing"]!.GetValue<string>(),
                p["pigType"]!.GetValue<string>(),
                p["specialInfo"]?.GetValue<string>() ?? "",
                cells));
        }
        return new SlidePigLevel(
            root["levelId"]!.GetValue<int>(),
            root["levelName"]?.GetValue<string>() ?? "",
            root["mapWidth"]!.GetValue<int>(),
            root["mapHeight"]!.GetValue<int>(),
            pigs);
    }
}

// 运行时实体：Type + 朝向（全程不变，道具翻转在 v1 范围外）+ 脚印 + 计数
public sealed record SlidePigEntity(
    int Id,                                            // 与关卡 JSON id 一致
    SlidePigType Type,                                 // 类型
    SlideFacing Facing,                                // 朝向（不可变）
    (int X, int Y)[] Cells,                            // 脚印格（已按 x,y 排序的规范形）
    int Counter);                                      // Boom=剩余爆炸数；Lock/Cage=剩余解锁数；其余恒 0

// 规则状态：在场实体 + 失败标志（爆炸猪计数归零 → 全局失败终态，无后继）
public sealed record SlidePigState(
    IReadOnlyList<SlidePigEntity> Pigs,                // 在场猪（按 Id 升序）
    bool Failed,                                       // true = 已触发失败（Boom 归零），无合法操作
    SlidePigLevel Level)                               // 所属关卡（棋盘尺寸与原始数据）
{
    // 找指定 id 的实体；无则 null
    public SlidePigEntity? Find(int id) => Pigs.FirstOrDefault(p => p.Id == id);
}

// 操作：点击指定 id 的猪（唯一操作形态；harness 侧映射为真实点击）
public sealed record SlidePigMove(int PigId);
