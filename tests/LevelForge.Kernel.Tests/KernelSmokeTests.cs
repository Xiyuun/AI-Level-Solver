namespace LevelForge.Kernel.Tests;

public class KernelSmokeTests
{
    [Fact]
    public void KernelAssemblyIsReferencedAndLoads()
    {
        var marker = typeof(LevelForge.Kernel.AssemblyMarker);
        Assert.Equal("LevelForge.Kernel", marker.Assembly.GetName().Name);
    }
}
