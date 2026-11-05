// 【插件契约】本程序集是平台与适配层插件共享的门面契约（三轮 D7 解冻）：
// 插件引用本程序集 + Kernel 编译，运行时由平台加载器保证与平台共享同一份类型定义。
// 命名空间保留 LevelForge.Platform.Facades 以零改动迁移——不是误放。
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
