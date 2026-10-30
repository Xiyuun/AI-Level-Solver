using System.Text.Json;
using System.Text.Json.Nodes;

namespace LevelForge.Adapters.SokobanToy;

// 玩具推箱子 · 输出校验器（接入产物 #2：格式校验函数）
// 双用途：① 生成端流水线校验 LLM 候选 JSON；② LoadLevel 前置完整校验
// 只判"格式与静态布局合法"，不判可解性——可解性由求解器裁决
public static class SokobanToyValidator
{
    // 合法字符集：墙/地板/玩家/箱/目标/箱在目标/玩家在目标
    private const string Chars = "# @$.+";

    // 校验网格布局，返回错误清单（空 = 通过）
    public static IReadOnlyList<string> Validate(string id, string[] grid)
    {
        var errors = new List<string>();
        // 基本形状
        if (grid is null || grid.Length == 0)
        {
            return [$"关卡 {id}：grid 为空"];
        }
        var width = grid[0].Length;
        if (width == 0)
        {
            errors.Add($"关卡 {id}：首行宽度为 0");
        }
        // 矩形 + 字符合法性
        var ragged = false;                                        // 非矩形标记：边界检查须跳过（会越界）
        for (var y = 0; y < grid.Length; y++)
        {
            var row = grid[y];
            if (row.Length != width)
            {
                ragged = true;
                errors.Add($"关卡 {id}：第 {y} 行宽度 {row.Length}，与首行 {width} 不一致（须矩形）");
            }
            foreach (var c in row)
            {
                if (!Chars.Contains(c))
                {
                    errors.Add($"关卡 {id}：第 {y} 行出现未知字符 '{c}'");
                }
            }
        }
        // 统计玩家/箱子/目标
        var players = 0;
        var boxes = 0;
        var goals = 0;
        for (var y = 0; y < grid.Length; y++)
        {
            for (var x = 0; x < grid[y].Length; x++)
            {
                var c = grid[y][x];
                if (c is '@' or '+') players++;
                if (c is '$' or '*') boxes++;
                if (c is '.' or '*' or '+') goals++;
            }
        }
        if (players != 1)
        {
            errors.Add($"关卡 {id}：玩家数 {players}，必须恰好 1 个（@ 或 +）");
        }
        if (boxes == 0)
        {
            errors.Add($"关卡 {id}：没有箱子（$ 或 *）");
        }
        if (boxes != goals)
        {
            errors.Add($"关卡 {id}：箱子数 {boxes} 与目标数 {goals} 不一致（标准胜利条件要求相等）");
        }
        // 边界墙：四边必须是墙，保证状态空间有界（非矩形网格跳过，宽度错误已记录）
        if (!ragged)
        {
            var lastRow = grid.Length - 1;
            for (var x = 0; x < width; x++)
            {
                if (grid[0][x] != '#') errors.Add($"关卡 {id}：顶边 ({x},0) 不是墙");
                if (grid[lastRow][x] != '#') errors.Add($"关卡 {id}：底边 ({x},{lastRow}) 不是墙");
            }
            for (var y = 0; y < grid.Length; y++)
            {
                if (grid[y][0] != '#') errors.Add($"关卡 {id}：左边 (0,{y}) 不是墙");
                if (grid[y][width - 1] != '#') errors.Add($"关卡 {id}：右边 ({width - 1},{y}) 不是墙");
            }
        }
        return errors;
    }

    // 校验候选关卡 JSON（生成端入口）：{"id":string,"grid":string[]}
    // 返回 (解析出的关卡, 错误清单)；错误为空时关卡可用
    public static (SokobanLevel? Level, IReadOnlyList<string> Errors) ValidateJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                return (null, ["候选不是 JSON 对象"]);
            }
            var id = obj["id"]?.GetValue<string>();
            var grid = obj["grid"]?.AsArray().Select(g => g?.GetValue<string>()).ToArray();
            if (string.IsNullOrWhiteSpace(id))
            {
                return (null, ["缺少字段 id（非空字符串）"]);
            }
            if (grid is null || grid.Any(g => g is null))
            {
                return (null, ["缺少字段 grid（字符串数组）或存在空行"]);
            }
            var errors = Validate(id, grid!);
            return errors.Count == 0 ? (new SokobanLevel(id!, grid!), errors) : (null, errors);
        }
        catch (JsonException ex)
        {
            return (null, [$"JSON 解析失败：{ex.Message}"]);
        }
    }
}
