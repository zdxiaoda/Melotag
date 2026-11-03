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

    /// <summary>能否原地改名。SAF 由 Document.COLUMN_FLAGS 决定，与平台无关。</summary>
    bool CanRename { get; }

    /// <summary>同目录标识。桌面=目录全路径；SAF 树内=父文档标识；无法枚举目录时为 null。</summary>
    string? DirectoryKey { get; }

    Stream OpenRead();
    Stream OpenReadWrite();

    /// <summary>同目录文件列表（含自身）。无法枚举时返回空集合。</summary>
    IReadOnlyList<IFileHandle> GetSiblings();

    /// <summary>原地改名（只传文件名，不含目录），返回改名后的新句柄。</summary>
    IFileHandle Rename(string newFileName);

    /// <summary>写入标签后恢复修改时间；平台不支持时静默忽略。</summary>
    void SetLastWriteTimeUtc(DateTime utc);
}
