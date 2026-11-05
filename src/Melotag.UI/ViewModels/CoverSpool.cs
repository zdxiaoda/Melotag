namespace Melotag.UI.ViewModels;

/// <summary>
/// 待写入封面的磁盘暂存。批量路径每次只把当前歌曲的封面读回内存，
/// 避免几千行同时持有原图字节。文件在替换、保存、清除、行释放或进程退出时删除。
/// </summary>
internal static class CoverSpool
{
    private static readonly string DirectoryPath =
        Path.Combine(Path.GetTempPath(), "melotag-cover-spool");

    private static readonly object Sync = new();
    private static readonly HashSet<string> Active = new(StringComparer.Ordinal);

    static CoverSpool()
    {
        // 清掉上次进程崩溃/被强制结束遗留的旧文件；只删一天前的，避免影响同时运行的另一个实例。
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                var cutoff = DateTime.UtcNow.AddDays(-1);
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.cover"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch
                    {
                        // 忽略单个文件清理失败。
                    }
                }
            }
        }
        catch
        {
            // 临时目录不可读时不影响正常流程。
        }

        // 正常退出时行对象通常不会被 Dispose，这里兜底清掉本次进程创建的暂存文件。
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (Sync)
            {
                foreach (var path in Active)
                {
                    try { File.Delete(path); } catch { }
                }

                Active.Clear();
            }
        };
    }

    public static string? TryStore(byte[] data)
    {
        if (data.Length == 0) return null;

        string? path = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            path = Path.Combine(DirectoryPath, $"{Guid.NewGuid():N}.cover");
            File.WriteAllBytes(path, data);

            lock (Sync)
                Active.Add(path);

            return path;
        }
        catch
        {
            // 写入失败时不要留下半截文件，也不要回退到内存（批量路径会因此常驻全部原图）。
            if (path is not null)
                TryDelete(path);
            return null;
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            File.Delete(path);
            lock (Sync)
                Active.Remove(path);
        }
        catch
        {
            // 文件可能仍被预览解码占用；保留在 Active 中，进程退出时再试。
        }
    }
}
