using System.Text;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Llm;

namespace LevelForge.Platform.Generation;

// 提示词组装（生成端出题分析 §3.1）：系统提示词 + few-shot + 本次参数 + 失败反馈
// 模板版本化管理：改模板必须升版本，落库到 generation_runs.prompt_version
public static class PromptBuilder
{
    public const string PromptVersion = "v1";   // 当前模板版本

    private const double Temperature = 0.8;     // 适中偏高：鼓励多样性（§5 温度与采样）

    // 基础消息：系统提示词（玩法规则书 + 格式说明书 + 输出约定）+ 用户消息（参数 + few-shot）
    public static List<ChatMessage> Build(
        GameRegistration registration, int targetBand,
        string? levelNumber, string? mechanicsNote, string? specialRequirements,
        IReadOnlyList<string> fewShot)
    {
        var system = $"""
            你是关卡设计师，为以下玩法设计一道新关卡。

            # 玩法规则
            {registration.GameBrief}

            # 关卡格式
            {registration.FormatSpec}

            # 输出约定
            只输出一道关卡的 JSON 原文：不要 markdown 代码块，不要任何解释文字，不要输出多道关卡。
            """;
        var user = new StringBuilder();
        user.AppendLine($"目标难度：第 {targetBand} 档（{DifficultyModel.BandName(targetBand)}）");
        if (!string.IsNullOrWhiteSpace(levelNumber))
        {
            user.AppendLine($"关卡编号：{levelNumber}");
            if (!string.IsNullOrWhiteSpace(mechanicsNote))
            {
                user.AppendLine($"该编号的机制范围：{mechanicsNote}");
            }
        }
        if (!string.IsNullOrWhiteSpace(specialRequirements))
        {
            user.AppendLine($"特殊要求：{specialRequirements}");
        }
        user.AppendLine();
        user.AppendLine("以下是该玩法的历史关卡示例（仅作格式与风格参考，禁止照抄布局）：");
        foreach (var shot in fewShot)
        {
            user.AppendLine(shot);
        }
        return [new ChatMessage("system", system), new ChatMessage("user", user.ToString())];
    }

    // 重生反馈（仅尝试 >1 时追加）：上一候选的失败分类与已验证事实 → 自然语言修正指令
    public static ChatMessage FeedbackMessage(int attemptIndex, string failureClass, string detail)
    {
        return new ChatMessage("user", $"""
            上一次候选（第 {attemptIndex} 次尝试）未通过验证，失败分类：{failureClass}。
            {detail}
            请重新设计一道全新的关卡。仍然只输出 JSON 原文。
            """);
    }

    // 修复重试（同一次尝试内，§3.2 上限 2 次）：把校验错误回喂，要求重新输出
    public static ChatMessage RepairMessage(IReadOnlyList<string> errors)
    {
        return new ChatMessage("user", $"""
            你上一条输出不是合法的关卡 JSON。校验错误：
            {string.Join("；", errors)}
            请重新输出：只输出一道合法关卡的 JSON 原文，严格按格式说明书。
            """);
    }

    // 机制范围提示：解析机制解锁表，返回覆盖该编号的区间说明；编号非数字或无匹配返回 null
    public static string? MechanicsNoteFor(string mechanicsJson, string? levelNumber)
    {
        if (levelNumber is null || !long.TryParse(levelNumber, out var number))
        {
            return null;
        }
        try
        {
            var table = JsonNode.Parse(mechanicsJson)?["ranges"]?.AsArray();
            if (table is null)
            {
                return null;
            }
            foreach (var range in table)
            {
                var from = range?["from"]?.GetValue<long>();
                var to = range?["to"]?.GetValue<long>();
                if (from is { } f && to is { } t && number >= f && number <= t)
                {
                    var mechanics = range!["mechanics"]?.AsArray()
                        .Select(m => m?.GetValue<string>()).Where(s => s is not null).ToList();
                    var note = range["note"]?.GetValue<string>();
                    var mechanicsText = mechanics is { Count: > 0 } ? $"可用机制：{string.Join("、", mechanics!)}" : "";
                    return string.Join("；", new[] { mechanicsText, note }.Where(s => !string.IsNullOrEmpty(s)));
                }
            }
            return null;   // 编号不在任何区间：不臆造范围，交由策划在界面上核对
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // 组装用的采样温度
    public static double TemperatureValue => Temperature;
}
