using System.Text.Json.Nodes;

namespace LevelForge.Adapters.SlidePig;

// 滑动猪 · 输出校验器（接入产物：格式校验函数）
// 双用途：① 生成端流水线校验 LLM 候选；② LoadLevel 前置完整校验
// 只判"格式与静态布局合法 + 类型在 v1 支持子集内"，不判可解性（求解器职责）
public static class SlidePigValidator
{
    // v1 支持的类型名（与适配器一致；Duck/Rope/Scissors/Farmer/Link/... 为范围外，明确报错不静默）
    private static readonly string[] SupportedTypes =
    {
        "Normal", "Boom", "Spring", "Long", "Lock", "SmallElephant", "Cage", "CageL"
    };

    private static readonly string[] SupportedFacings = { "Up", "Left", "Down", "Right" };

    // 校验已解析关卡，返回错误清单（空 = 通过）
    public static IReadOnlyList<string> Validate(SlidePigLevel level)
    {
        var errors = new List<string>();
        if (level.MapWidth <= 0 || level.MapHeight <= 0 || level.MapWidth > 50 || level.MapHeight > 50)
        {
            errors.Add($"棋盘尺寸非法：{level.MapWidth}×{level.MapHeight}（须在 1~50）");
        }
        if (level.Pigs.Count == 0)
        {
            errors.Add("猪列表为空");
        }
        if (level.Pigs.Count != level.Pigs.Select(p => p.Id).Distinct().Count())
        {
            errors.Add("存在重复猪 id");
        }
        var pigFootprint = new HashSet<string>();      // 非铁笼脚印（猪与猪不得重叠）
        var cageFootprint = new HashSet<string>();     // 铁笼脚印（笼与笼不得重叠；笼与猪可叠放=关猪）
        foreach (var p in level.Pigs)
        {
            if (!SupportedTypes.Contains(p.PigType))
            {
                errors.Add($"猪 #{p.Id} 类型 {p.PigType} 不在 v1 支持子集（Normal/Boom/Spring/Long/Lock/SmallElephant/Cage/CageL）");
            }
            if (!SupportedFacings.Contains(p.Facing))
            {
                errors.Add($"猪 #{p.Id} 朝向非法：{p.Facing}");
            }
            if (p.OccupiedCells.Length == 0)
            {
                errors.Add($"猪 #{p.Id} 脚印为空");
            }
            var isCage = p.PigType is "Cage" or "CageL";
            var target = isCage ? cageFootprint : pigFootprint;
            foreach (var c in p.OccupiedCells)
            {
                if (c.X < 0 || c.X >= level.MapWidth || c.Y < 0 || c.Y >= level.MapHeight)
                {
                    errors.Add($"猪 #{p.Id} 脚印格 ({c.X},{c.Y}) 越界");
                }
                if (!target.Add($"{c.X},{c.Y}"))
                {
                    errors.Add(isCage
                        ? $"铁笼 #{p.Id} 脚印格 ({c.X},{c.Y}) 与其他铁笼重叠"
                        : $"猪 #{p.Id} 脚印格 ({c.X},{c.Y}) 与其他猪重叠");
                }
            }
            // 计数字段可解析性（允许空串走默认值，非数字报错）
            if ((p.PigType is "Boom" or "Lock" or "Cage" or "CageL")
                && !string.IsNullOrWhiteSpace(p.SpecialInfo)
                && !int.TryParse(p.SpecialInfo.Trim(), out _))
            {
                errors.Add($"猪 #{p.Id} specialInfo 非整数：\"{p.SpecialInfo}\"");
            }
        }
        return errors;
    }

    // 校验候选关卡 JSON 原文（生成端入口）：解析 + 静态校验一步完成
    public static (SlidePigLevel? Level, IReadOnlyList<string> Errors) ValidateJson(string json)
    {
        try
        {
            var level = SlidePigLevel.FromJson(json);
            var errors = Validate(level);
            return errors.Count == 0 ? (level, errors) : (null, errors);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentNullException
            or System.Text.Json.JsonException or NullReferenceException)
        {
            return (null, [$"JSON 结构非法：{ex.Message}"]);
        }
    }
}
