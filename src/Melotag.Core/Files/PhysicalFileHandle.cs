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
        var info = new FileInfo(full);
        Length = info.Exists ? info.Length : 0;
        LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.UnixEpoch;
        CreationTimeUtc = info.Exists && info.CreationTimeUtc > DateTime.UnixEpoch
            ? info.CreationTimeUtc
            : null;
    }

    public string DisplayPath { get; }
    public string FileName { get; }
    public string FileNameWithoutExtension { get; }
    public string Extension { get; }
    public long Length { get; }
    public DateTime LastWriteTimeUtc { get; }
    public DateTime? CreationTimeUtc { get; }
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

        return Directory.EnumerateFiles(dir)
            .Select(file => (IFileHandle)new PhysicalFileHandle(file))
            .ToList();
    }

    public IFileHandle Rename(string newFileName)
    {
        if (string.IsNullOrWhiteSpace(newFileName) ||
            !string.Equals(Path.GetFileName(newFileName), newFileName, StringComparison.Ordinal))
            throw new ArgumentException("文件名不能包含目录", nameof(newFileName));

        var directory = Path.GetDirectoryName(DisplayPath) ?? ".";
        var target = Path.Combine(directory, newFileName);
        File.Move(DisplayPath, target);
        return new PhysicalFileHandle(target);
    }

    public void SetLastWriteTimeUtc(DateTime utc)
    {
        if (!File.Exists(DisplayPath)) return;
        File.SetLastWriteTimeUtc(DisplayPath, utc);
    }

    public override string ToString() => DisplayPath;
}
