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

    /// <summary>稳定标识（历史记录 / 去重键）。桌面为完整路径，SAF 为 content:// URI。</summary>
    string Key { get; }

    /// <summary>是否可写回。SAF 只读授权时为 false。</summary>
    bool IsWritable { get; }

    Stream OpenRead();
    Stream OpenReadWrite();

    /// <summary>同目录下按文件名找兄弟文件（用于 .lrc / 封面），不存在返回 null。</summary>
    IFileHandle? GetSibling(string fileName);

    /// <summary>写入标签后恢复修改时间；平台不支持时静默忽略。</summary>
    void SetLastWriteTimeUtc(DateTime utc);
}
