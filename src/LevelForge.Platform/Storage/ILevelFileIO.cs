namespace LevelForge.Platform.Storage;

// 文件 IO 接缝：安全落盘的可注入 IO 层（故障注入测试替换 Move 制造"半写"事故）
public interface ILevelFileIO
{
    bool Exists(string path);                       // 文件存在
    byte[] ReadAllBytes(string path);               // 读原始字节
    void WriteAllBytes(string path, byte[] bytes);  // 写文件
    void MoveOverwrite(string source, string target);   // 原子替换（同卷 Move+overwrite）
    void Delete(string path);                       // 删除（临时文件清理）
}

// 生产实现：直通文件系统；方法虚化供测试注入故障
public class FileLevelFileIO : ILevelFileIO
{
    public virtual bool Exists(string path) => File.Exists(path);

    public virtual byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public virtual void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public virtual void MoveOverwrite(string source, string target) => File.Move(source, target, overwrite: true);

    public virtual void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
