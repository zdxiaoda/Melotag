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
    }

    public string DisplayPath { get; }
    public string FileName { get; }
    public string FileNameWithoutExtension { get; }
    public string Extension { get; }
    public long Length { get; }
    public DateTime LastWriteTimeUtc { get; }

    public Stream OpenRead() =>
        new FileStream(DisplayPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public Stream OpenReadWrite() =>
        new FileStream(DisplayPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

    public IFileHandle? GetSibling(string fileName)
    {
        var dir = Path.GetDirectoryName(DisplayPath);
        if (dir is null) return null;
        var candidate = Path.Combine(dir, fileName);
        return File.Exists(candidate) ? new PhysicalFileHandle(candidate) : null;
    }

    public override string ToString() => DisplayPath;
}
