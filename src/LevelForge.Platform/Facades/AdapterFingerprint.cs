using System.Reflection;
using System.Security.Cryptography;

namespace LevelForge.Platform.Facades;

// 适配层产物指纹：适配层程序集文件字节的 SHA-256
// 用途：验证记录/基准按指纹失效（验证门 §2.1）——适配产物一变，旧证据全部作废重跑
public static class AdapterFingerprint
{
    // 输出 sha256:<小写hex>；与 TapeHashes 的摘要格式约定一致
    public static string Of(Assembly assembly)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
