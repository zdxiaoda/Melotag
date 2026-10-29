namespace Melotag.Storage;

using Melotag.Core.Abstractions;

/// <summary>
/// 桌面平台能力：数据目录在用户配置目录，支持原地重命名，
/// 封面导出到音频同目录的 "文件名-cover.jpg/png"。
/// </summary>
public class FileSystemPlatformHost : IPlatformHost
{
    public string DataDirectory => AppPaths.DataDirectory;

    public virtual bool SupportsRename => true;

    public virtual bool SupportsSiblingFiles => true;

    public virtual Task<string?> ExportCoverAsync(IFileHandle audio, byte[] data, string extension)
    {
        var directory = Path.GetDirectoryName(audio.DisplayPath);
        if (directory is null) return Task.FromResult<string?>(null);

        var target = Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(audio.DisplayPath) + "-cover" + extension);

        File.WriteAllBytes(target, data);
        return Task.FromResult<string?>(target);
    }
}
