namespace Melotag.Core.Files;

using Melotag.Core.Abstractions;

/// <summary>桌面实现：真实文件系统路径。</summary>
public sealed class PhysicalFileHandle : IFileHandle
{
    public PhysicalFileHandle(string path)
    {
        var full = Path.GetFullPath(path);
        DisplayPath = full;
        FileName = Path.GetFileName(full);
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(full);
        Extension = Path.GetExtension(full).ToLowerInvariant();
        RefreshMetadata();
    }

    public string DisplayPath { get; }
    public string FileName { get; }
    public string FileNameWithoutExtension { get; }
    public string Extension { get; }
    public long Length { get; private set; }
    public DateTime LastWriteTimeUtc { get; private set; }
    public DateTime? CreationTimeUtc { get; private set; }
    public string Key => DisplayPath;
    public bool IsWritable => true;
    public bool CanRename => true;
    public string? DirectoryKey => Path.GetDirectoryName(DisplayPath);

    public Stream OpenRead() =>
        new FileStream(DisplayPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public Stream OpenReadWrite() =>
        new FileStream(DisplayPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

    public IReadOnlyList<IFileHandle> GetSiblings()
    {
        var dir = Path.GetDirectoryName(DisplayPath);
        if (dir is null || !Directory.Exists(dir)) return [];

        try
        {
            return Directory.EnumerateFiles(dir)
                .Select(file => (IFileHandle)new PhysicalFileHandle(file))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IFileHandle Rename(string newFileName)
    {
        if (string.IsNullOrWhiteSpace(newFileName) ||
            newFileName is "." or ".." ||
            !string.Equals(Path.GetFileName(newFileName), newFileName, StringComparison.Ordinal))
            throw new ArgumentException("文件名不能包含目录", nameof(newFileName));

        var directory = Path.GetDirectoryName(DisplayPath) ?? ".";
        var target = Path.Combine(directory, newFileName);

        // 大小写不敏感的文件系统（Windows/macOS）上直接 Move 到仅大小写不同的名字会失败或无效，
        // 先挪到同目录临时名再挪到目标。
        if (IsCaseOnlyChange(newFileName))
        {
            var temp = Path.Combine(directory, $"melotag-{Guid.NewGuid():N}{Extension}");
            File.Move(DisplayPath, temp);
            File.Move(temp, target);
            return new PhysicalFileHandle(target);
        }

        // 不覆盖：目标已存在时 File.Move 会抛 IOException
        File.Move(DisplayPath, target);
        return new PhysicalFileHandle(target);
    }

    /// <summary>目标名与当前名只有大小写差异（Ordinal 不同但 OrdinalIgnoreCase 相同）。</summary>
    private bool IsCaseOnlyChange(string newFileName) =>
        string.Equals(newFileName, FileName, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(newFileName, FileName, StringComparison.Ordinal);

    public void SetLastWriteTimeUtc(DateTime utc)
    {
        if (!File.Exists(DisplayPath)) return;
        File.SetLastWriteTimeUtc(DisplayPath, utc);
        LastWriteTimeUtc = utc;
    }

    public void RefreshMetadata()
    {
        var info = new FileInfo(DisplayPath);
        if (!info.Exists)
        {
            Length = 0;
            LastWriteTimeUtc = DateTime.UnixEpoch;
            CreationTimeUtc = null;
            return;
        }

        Length = info.Length;
        LastWriteTimeUtc = info.LastWriteTimeUtc;
        CreationTimeUtc = info.CreationTimeUtc > DateTime.UnixEpoch ? info.CreationTimeUtc : null;
    }

    public override string ToString() => DisplayPath;
}
