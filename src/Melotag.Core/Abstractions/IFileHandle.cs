namespace Melotag.Core.Abstractions;

/// <summary>
/// 文件句柄抽象。桌面用真实路径实现；Android 用 SAF 文档实现，
/// 上层代码不得直接依赖 System.IO.File。
/// </summary>
public interface IFileHandle
{
    string DisplayPath { get; }
    string FileName { get; }
    string FileNameWithoutExtension { get; }
    /// <summary>含点，小写，例如 ".flac"。</summary>
    string Extension { get; }
    long Length { get; }
    DateTime LastWriteTimeUtc { get; }

    Stream OpenRead();
    Stream OpenReadWrite();

    /// <summary>同目录下按文件名找兄弟文件（用于 .lrc / 封面），不存在返回 null。</summary>
    IFileHandle? GetSibling(string fileName);
}
