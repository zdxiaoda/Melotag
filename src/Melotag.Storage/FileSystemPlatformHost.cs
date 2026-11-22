namespace Melotag.Storage;

using Melotag.Core.Abstractions;
using System.Globalization;

/// <summary>
/// 桌面平台能力：数据目录在用户配置目录，
/// 封面导出到音频同目录的 "文件名-cover.jpg/png"。
/// </summary>
public class FileSystemPlatformHost : IPlatformHost
{
    public string DataDirectory => AppPaths.DataDirectory;

    public virtual Task<string?> ExportCoverAsync(IFileHandle audio, byte[] data, string extension)
    {
        var directory = Path.GetDirectoryName(audio.DisplayPath);
        if (string.IsNullOrEmpty(directory)) return Task.FromResult<string?>(null);

        var baseName = Path.GetFileNameWithoutExtension(audio.DisplayPath) + "-cover";

        // 批量导出封面时不要在 UI 线程写盘
        return Task.Run(() =>
        {
            // 已存在同名封面时追加 (2)(3)…，避免静默覆盖用户已有的封面文件
            for (var index = 1; index <= 100; index++)
            {
                var fileName = index == 1
                    ? baseName + extension
                    : baseName + " (" + index.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                var target = Path.Combine(directory, fileName);

                try
                {
                    using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(data, 0, data.Length);
                    return (string?)target;
                }
                catch (IOException) when (File.Exists(target))
                {
                    // 名称已占用，换下一个序号
                }
            }

            return null;
        });
    }
}
