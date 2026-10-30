using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Hashing;

// 四类摘要的独立用途与取值规则（渲染规范 §5.1）：
// levelHash           关卡文件原始字节——绑定这份关卡，写盘/编辑后旧验证失效
// semanticStateHash   语义投影——逐步对拍，差异时展示原始字段而不只报哈希不等
// trajectoryHash      轨迹——同输入/同版本/同配置的确定性检查，排除 moveText 与视觉表现
// artifactHash        磁带文件完整性——版本、文案、视觉变化都会引起变化，不自动视为规则回退
public static class TapeHashes
{
    private const string HashPrefix = "sha256:";   // 摘要统一前缀，输出形如 sha256:<小写hex>

    #region 四类摘要

    // 关卡哈希：对实际导入/导出文件的原始字节计算，不做 JSON 规范化
    public static string LevelHash(byte[] levelFileBytes)
    {
        return HashPrefix + HexLower(SHA256.HashData(levelFileBytes));
    }

    // 语义状态哈希：semanticSchemaVer + 规范化语义投影
    public static string SemanticStateHash(int semanticSchemaVer, JsonNode semanticState)
    {
        var payload = new JsonObject
        {
            ["semanticSchemaVer"] = semanticSchemaVer,
            ["state"] = CanonicalJson.Canonicalize(semanticState)
        };
        return HashCanonical(payload);
    }

    // 轨迹哈希：两个 schema 版本 + 初始语义状态 + 按序的 (move, semanticState) + outcome
    // 刻意排除 moveText 与场景快照——展示与视觉变化不得影响行为摘要
    public static string TrajectoryHash(
        int moveSchemaVer,
        int semanticSchemaVer,
        JsonNode initialSemanticState,
        IReadOnlyList<(JsonNode Move, JsonNode SemanticState)> steps,
        ExecutionOutcome outcome)
    {
        var stepArray = new JsonArray();
        foreach (var (move, semanticState) in steps)
        {
            // 步序数组顺序承载轨迹语义，规范化不会重排（非实体集合）
            stepArray.Add(new JsonObject
            {
                ["move"] = CanonicalJson.Canonicalize(move),
                ["semanticState"] = CanonicalJson.Canonicalize(semanticState)
            });
        }
        var payload = new JsonObject
        {
            ["moveSchemaVer"] = moveSchemaVer,
            ["semanticSchemaVer"] = semanticSchemaVer,
            ["initial"] = CanonicalJson.Canonicalize(initialSemanticState),
            ["steps"] = stepArray,
            ["outcome"] = outcome.ToString()
        };
        return HashCanonical(payload);
    }

    // 磁带完整性哈希：除 artifactHash 字段本身外的规范化磁带内容——无自包含哈希循环
    public static string ArtifactHash(JsonObject tape)
    {
        // 规范化副本即独立副本（无父级），可直接改写 artifactHash 字段
        var copy = (JsonObject)CanonicalJson.Canonicalize(tape)!;
        // 统一置 null 再参与计算，无论原磁带是否已写入摘要值；无自包含哈希循环
        copy["artifactHash"] = null;
        return HashCanonical(copy);
    }

    #endregion

    #region 内部工具

    // 规范化后取 SHA-256，统一输出小写 hex
    private static string HashCanonical(JsonNode payload)
    {
        var canonical = CanonicalJson.Serialize(payload);
        return HashPrefix + HexLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    // 字节数组转小写十六进制
    private static string HexLower(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    #endregion
}
