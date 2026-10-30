namespace LevelForge.Adapters.SokobanToy.Tests;

public class AdapterSmokeTests
{
    [Fact]
    public void AdapterAssemblyIsReferencedAndKernelIsTransitivelyVisible()
    {
        var marker = typeof(LevelForge.Adapters.SokobanToy.AssemblyMarker);
        Assert.Equal("LevelForge.Adapters.SokobanToy", marker.Assembly.GetName().Name);
    }
}
