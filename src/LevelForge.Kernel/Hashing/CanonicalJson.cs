using System.Text.Json.Nodes;

namespace LevelForge.Kernel.Hashing;

// 规范化 JSON：哈希与语义比较的统一输入（渲染规范 §5.1）
// 规则：对象键递归按字典序（Ordinal）排列；数组保留协议顺序，唯一例外是实体集合
// （元素均为含字符串 id 的对象）按 id 稳定排序；坐标和计数使用整数；无空白紧凑输出
// 其他数值类型（浮点等）须在 semanticSchema 中先固定规范化方式，才能参与对拍
public static class CanonicalJson
{
    // 输出规范化字符串：语义相等的 JSON（仅键序/实体序不同）产生相同字符串
    public static string Serialize(JsonNode? node)
    {
        // 先规范化为独立副本再紧凑序列化，避免修改调用方节点
        var canonical = Canonicalize(node);
        return canonical?.ToJsonString() ?? "null";
    }

    // 返回深拷贝的规范化节点：不修改入参，产出节点无父级可安全挂载
    public static JsonNode? Canonicalize(JsonNode? node)
    {
        return CanonicalizeNode(node);
    }

    #region 规范化内部实现

    // 递归规范化单个节点
    private static JsonNode? CanonicalizeNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                return CanonicalizeObject(obj);
            case JsonArray arr:
                return CanonicalizeArray(arr);
            case null:
                return null;
            default:
                // 值节点：经文本往返取得独立副本，保留数字原始文本（1 不变 1.0）
                return JsonNode.Parse(node.ToJsonString());
        }
    }

    // 对象：键按 Ordinal 排序后逐键重建
    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        // OrderBy 稳定排序，保证同键集合输出确定
        foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            result[kvp.Key] = CanonicalizeNode(kvp.Value);
        }
        return result;
    }

    // 数组：实体集合按 id 排序，其余保持协议顺序
    private static JsonArray CanonicalizeArray(JsonArray arr)
    {
        var result = new JsonArray();
        if (TryGetEntityIds(arr, out var ids))
        {
            // 实体集合：按 id 排序，同 id 时按原序保证确定性
            var indexed = arr.Select((n, i) => (Node: n, Index: i)).ToList();
            foreach (var item in indexed.OrderBy(t => ids[t.Index], StringComparer.Ordinal).ThenBy(t => t.Index))
            {
                result.Add(CanonicalizeNode(item.Node));
            }
        }
        else
        {
            // 非实体数组：顺序本身承载语义（步序、序列），原样保留
            foreach (var item in arr)
            {
                result.Add(CanonicalizeNode(item));
            }
        }
        return result;
    }

    // 判定实体集合并提取各元素的 id：全部元素均为含字符串 id 的对象才成立
    private static bool TryGetEntityIds(JsonArray arr, out string?[] ids)
    {
        ids = new string?[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            // 任一元素不是"含字符串 id 的对象"即判为普通数组
            if (arr[i] is not JsonObject obj ||
                obj["id"] is not JsonValue idVal ||
                !idVal.TryGetValue<string>(out var id) ||
                id is null)
            {
                return false;
            }
            ids[i] = id;
        }
        return true;
    }

    #endregion
}
