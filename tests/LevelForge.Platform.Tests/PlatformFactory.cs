using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 平台测试工厂：默认每实例独立临时数据目录（测试间零共享、可并行）；
// 显式传入 dataDir 可复刻"进程重启"场景（两个工厂先后用同一目录）
public sealed class PlatformFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir;
    private readonly bool _ownsDir;

    public PlatformFactory(string? dataDir = null)
    {
        if (dataDir is null)
        {
            _dataDir = Path.Combine(Path.GetTempPath(), "levelforge-test-" + Guid.NewGuid().ToString("N"));
            _ownsDir = true;
        }
        else
        {
            _dataDir = dataDir;   // 共享目录：清理交给创建者
            _ownsDir = false;
        }
    }

    // 供共享目录场景使用的目录路径
    public string DataDir => _dataDir;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 注入数据目录（Program 从配置读取 LevelForge:DataDir）
        builder.UseSetting("LevelForge:DataDir", _dataDir);
    }

    // 数据库直连（断言落库内容用）
    public AppDatabase Database => Services.GetRequiredService<AppDatabase>();

    // 玩法注册表（断言指纹对齐用）
    public GameRegistry Registry => Services.GetRequiredService<GameRegistry>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // 清理尽力而为：临时目录残留不影响下次测试（唯一目录名）
        if (_ownsDir)
        {
            try
            {
                if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
            }
            catch (IOException) { /* 文件句柄延迟释放时忽略 */ }
        }
    }
}
