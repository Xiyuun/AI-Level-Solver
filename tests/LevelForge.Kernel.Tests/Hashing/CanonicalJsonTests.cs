using System.Text.Json.Nodes;
using LevelForge.Kernel.Hashing;
using Xunit;

namespace LevelForge.Kernel.Tests.Hashing;

// CanonicalJson 规范化规则的验收测试（渲染规范 §5.1 每条规则至少一个用例）
public class CanonicalJsonTests
{
    [Fact]
    public void Serialize_对象键递归排序_输出确定()
    {
        // 同一对象两种键序，规范化后必须逐字节相同
        var a = JsonNode.Parse("{\"b\":{\"d\":1,\"c\":2},\"a\":3}")!;
        var b = JsonNode.Parse("{\"a\":3,\"b\":{\"c\":2,\"d\":1}}")!;
        Assert.Equal(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Serialize_实体集合按稳定id排序()
    {
        // entities 数组元素均含字符串 id：按 id 排序，键序差异一并消除
        var a = JsonNode.Parse("{\"entities\":[{\"id\":\"B\",\"x\":1},{\"id\":\"A\",\"x\":2}]}")!;
        var b = JsonNode.Parse("{\"entities\":[{\"id\":\"A\",\"x\":2},{\"id\":\"B\",\"x\":1}]}")!;
        Assert.Equal(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Serialize_普通数组保序_顺序差异可见()
    {
        // 非实体数组：顺序承载语义，重排必须产生不同输出
        var a = JsonNode.Parse("[3,1,2]")!;
        var b = JsonNode.Parse("[1,2,3]")!;
        Assert.NotEqual(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Serialize_无id对象数组保序()
    {
        // 元素缺字符串 id 的对象数组不按实体集合处理：保持原序
        var a = JsonNode.Parse("[{\"type\":\"push\"},{\"type\":\"slide\"}]")!;
        var b = JsonNode.Parse("[{\"type\":\"slide\"},{\"type\":\"push\"}]")!;
        Assert.NotEqual(CanonicalJson.Serialize(a), CanonicalJson.Serialize(b));
    }

    [Fact]
    public void Serialize_混合数组不判为实体集合()
    {
        // 部分元素无 id：整体退化为普通数组，保序
        var a = JsonNode.Parse("[{\"id\":\"A\"},{}]")!;
        Assert.Equal("[{\"id\":\"A\"},{}]", CanonicalJson.Serialize(a));
    }

    [Fact]
    public void Serialize_紧凑输出无空白()
    {
        var node = JsonNode.Parse("{ \"a\" : 1 ,  \"b\" : [ 2 , 3 ] }")!;
        Assert.Equal("{\"a\":1,\"b\":[2,3]}", CanonicalJson.Serialize(node));
    }

    [Fact]
    public void Serialize_整数字段保持整数字面量()
    {
        var node = JsonNode.Parse("{\"x\":1}")!;
        Assert.Equal("{\"x\":1}", CanonicalJson.Serialize(node));
    }

    [Fact]
    public void Canonicalize_不修改入参()
    {
        // 原始节点保持插入序，规范化产出是独立副本
        var node = (JsonObject)JsonNode.Parse("{\"b\":1,\"a\":2}")!;
        var keysBefore = string.Join(",", node.Select(k => k.Key));
        _ = CanonicalJson.Canonicalize(node);
        Assert.Equal("b,a", keysBefore);
        Assert.Equal("b,a", string.Join(",", node.Select(k => k.Key)));
    }

    [Fact]
    public void Canonicalize_返回无父级可挂载的副本()
    {
        // 规范化结果可以安全挂进新容器（JsonNode 单父级约束）
        var node = JsonNode.Parse("{\"a\":1}")!;
        var canonical = CanonicalJson.Canonicalize(node)!;
        var holder = new JsonObject { ["inner"] = canonical };
        Assert.NotNull(holder["inner"]);
    }
}
