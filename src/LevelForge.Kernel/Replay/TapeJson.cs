using System.Text.Json;
using System.Text.Json.Serialization;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Replay;

// 磁带 JSON 序列化：磁带文件的唯一读写格式
// 字段名 camelCase、枚举输出小写字符串（win/lose/incomplete/error），与渲染规范 §5 字段名一致
public static class TapeJson
{
    // 序列化选项：Web 默认（camelCase + 忽略大小写）+ 枚举小写字符串
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // 磁带 → JSON 字符串（含 artifactHash，紧凑输出）
    public static string Serialize(SolutionTape tape)
    {
        return JsonSerializer.Serialize(tape, Options);
    }

    // JSON 字符串 → 磁带；格式非法抛 JsonException
    public static SolutionTape Parse(string json)
    {
        return JsonSerializer.Deserialize<SolutionTape>(json, Options)
            ?? throw new JsonException("磁带内容为空");
    }
}
