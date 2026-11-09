namespace LevelForge.Adapters.PiggyFarm;

// 猪类型目录：主玩法 18 种猪的中文/英文别名 + v1 支持状态。
// 生成需求解析、关卡校验、渲染映射共用同一份名字，避免"爆炸猪/boom/爆炸"各写一套。
public static class PiggyFarmPigTypes
{
    // 主玩法全部 PigType（DataModel/Pig.cs）；v1.1 实现普通滑动家族 + 爆炸计数 + 小黄鸭自动离场
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.Ordinal) { "Normal", "Long", "RichPig", "Boom", "Duck" };

    public static readonly IReadOnlySet<string> Unsupported = new HashSet<string>(StringComparer.Ordinal)
    {
        "Spring", "TimeBoom", "Lock", "Farmer", "Balloon", "RopeLong", "Scissors",
        "Sleep", "Cage", "CageL", "Link", "Zombie", "BackpackSquirrel", "Rabbit", "JokerBox",
    };

    // 中文/英文别名 → 规范类型名（大小写不敏感）
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["普通猪"] = "Normal", ["普通"] = "Normal", ["正常猪"] = "Normal", ["Normal"] = "Normal", ["normal"] = "Normal",
        ["长猪"] = "Long", ["大长猪"] = "Long", ["长条猪"] = "Long", ["大长"] = "Long", ["Long"] = "Long", ["long"] = "Long",
        ["土豪猪"] = "RichPig", ["财主猪"] = "RichPig", ["土豪"] = "RichPig", ["RichPig"] = "RichPig", ["Rich"] = "RichPig",
        ["爆炸猪"] = "Boom", ["爆炸"] = "Boom", ["炸弹猪"] = "Boom", ["炸弹"] = "Boom",
        ["Boom"] = "Boom", ["BoomPig"] = "Boom",
        ["弹簧猪"] = "Spring", ["弹簧"] = "Spring", ["Spring"] = "Spring",
        ["定时爆炸猪"] = "TimeBoom", ["定时爆炸"] = "TimeBoom",
        ["定时炸弹猪"] = "TimeBoom", ["定时炸弹"] = "TimeBoom", ["定时"] = "TimeBoom",
        ["TimeBoom"] = "TimeBoom", ["TimeBomb"] = "TimeBoom",
        ["锁猪"] = "Lock", ["锁"] = "Lock", ["Lock"] = "Lock", ["LockPig"] = "Lock",
        ["农场主"] = "Farmer", ["Farmer"] = "Farmer",
        ["气球猪"] = "Balloon", ["气球"] = "Balloon", ["Balloon"] = "Balloon",
        ["捆绑长猪"] = "RopeLong", ["长捆绑猪"] = "RopeLong", ["绳子长猪"] = "RopeLong", ["RopeLong"] = "RopeLong",
        ["剪刀猪"] = "Scissors", ["剪刀"] = "Scissors", ["Scissors"] = "Scissors", ["Scissor"] = "Scissors",
        ["小黄鸭"] = "Duck", ["鸭子"] = "Duck", ["鸭"] = "Duck", ["Duck"] = "Duck", ["DuckPig"] = "Duck",
        ["睡觉猪"] = "Sleep", ["睡觉"] = "Sleep", ["睡猪"] = "Sleep", ["Sleep"] = "Sleep",
        ["铁笼"] = "Cage", ["笼子"] = "Cage", ["Cage"] = "Cage", ["CageL"] = "CageL", ["缺角铁笼"] = "CageL",
        ["链接猪"] = "Link", ["连接猪"] = "Link", ["链接"] = "Link", ["连接"] = "Link", ["Link"] = "Link",
        ["丧尸猪"] = "Zombie", ["僵尸猪"] = "Zombie", ["丧尸"] = "Zombie", ["僵尸"] = "Zombie", ["Zombie"] = "Zombie",
        ["背包松鼠"] = "BackpackSquirrel", ["松鼠"] = "BackpackSquirrel", ["Squirrel"] = "BackpackSquirrel", ["BackpackSquirrel"] = "BackpackSquirrel",
        ["兔子"] = "Rabbit", ["兔"] = "Rabbit", ["Rabbit"] = "Rabbit",
    };

    public static string? Resolve(string? text)
    {
        var key = text?.Trim();
        if (string.IsNullOrEmpty(key)) return null;
        if (Alias.TryGetValue(key, out var exact)) return exact;

        // 常见中文变体/组合词兜底：先判"定时炸弹"再判"炸弹"，避免 TimeBoom 被吃成 Boom
        if (key.Contains("定时") && (key.Contains("炸弹") || key.Contains("爆炸"))) return "TimeBoom";
        if (key.Contains("炸弹") || key.Contains("爆炸")) return "Boom";
        if (key.Contains("弹簧")) return "Spring";
        if (key.Contains("锁")) return "Lock";
        if (key.Contains("农场主")) return "Farmer";
        if (key.Contains("气球")) return "Balloon";
        if (key.Contains("捆绑") || key.Contains("绳子")) return "RopeLong";
        if (key.Contains("剪刀")) return "Scissors";
        if (key.Contains("鸭")) return "Duck";
        if (key.Contains("睡")) return "Sleep";
        if (key.Contains("铁笼") || key.Contains("笼")) return "Cage";
        if (key.Contains("链接") || key.Contains("连接")) return "Link";
        if (key.Contains("丧尸") || key.Contains("僵尸")) return "Zombie";
        if (key.Contains("松鼠")) return "BackpackSquirrel";
        if (key.Contains("兔")) return "Rabbit";
        if (key.Contains("土豪") || key.Contains("财主")) return "RichPig";
        if (key.Contains("普通") || key.Contains("正常")) return "Normal";
        if (key.Contains("大长") || key.Contains("长条") || key == "长") return "Long";
        return null;
    }

    public static string DisplayName(string type) => type switch
    {
        "Normal" => "普通猪",
        "Long" => "大长猪",
        "RichPig" => "土豪猪",
        "Boom" => "爆炸猪",
        "Spring" => "弹簧猪",
        "TimeBoom" => "定时爆炸猪",
        "Lock" => "锁猪",
        "Farmer" => "农场主",
        "Balloon" => "气球猪",
        "RopeLong" => "捆绑长猪",
        "Scissors" => "剪刀猪",
        "Duck" => "小黄鸭",
        "Sleep" => "睡觉猪",
        "Cage" => "铁笼",
        "CageL" => "铁笼（缺角）",
        "Link" => "链接猪",
        "Zombie" => "丧尸猪",
        "BackpackSquirrel" => "背包松鼠",
        "Rabbit" => "兔子",
        _ => type,
    };
}
