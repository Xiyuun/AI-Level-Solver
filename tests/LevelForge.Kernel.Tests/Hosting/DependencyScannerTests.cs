using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Search;
using Xunit;

namespace LevelForge.Kernel.Tests.Hosting;

// 依赖扫描测试：引擎引用判定与程序集扫描
public class DependencyScannerTests
{
    [Theory]
    [InlineData("UnityEngine.CoreModule")]     // Unity 运行时
    [InlineData("UnityEditor")]                // Unity 编辑器
    [InlineData("unity.engine")]               // 大小写不敏感
    [InlineData("CocosSharp")]                 // Cocos
    [InlineData("cocos2d-x")]
    [InlineData("Godot")]
    [InlineData("MonoGame.Framework")]
    public void 引擎引用名_判定命中(string name)
    {
        Assert.True(DependencyScanner.IsEngineReference(name));
    }

    [Theory]
    [InlineData("System.Text.Json")]           // BCL
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("netstandard")]
    [InlineData("Newtonsoft.Json")]            // 普通第三方库（备注项，非拒绝项）
    [InlineData("AutoMapper")]
    public void 中性引用名_不命中(string name)
    {
        Assert.False(DependencyScanner.IsEngineReference(name));
    }

    [Fact]
    public void 内核程序集扫描_通过()
    {
        // 内核自身只引用 BCL，应通过且无引擎引用
        var result = DependencyScanner.Scan(typeof(AdapterHosting).Assembly);
        Assert.True(result.Passed);
        Assert.Empty(result.Issues);
    }
}
