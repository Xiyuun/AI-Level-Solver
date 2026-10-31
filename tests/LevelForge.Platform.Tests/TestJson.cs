using System.Text.Json.Serialization;

namespace LevelForge.Platform.Tests;

// 测试侧 JSON 口径：与平台 API 一致（camelCase + 枚举小写字符串）
internal static class TestJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        };
}
