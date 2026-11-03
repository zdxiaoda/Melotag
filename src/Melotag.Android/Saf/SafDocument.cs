namespace Melotag.Droid;

using AndroidUri = Android.Net.Uri;

/// <summary>SAF 文档元数据。TreeUri 为 null 表示单文档授权，无法枚举兄弟文件。</summary>
internal sealed record SafDocument(
    AndroidUri Uri,
    string DocId,
    string DisplayName,
    long Size,
    long LastModifiedMs,
    bool CanWrite,
    bool CanRename,
    AndroidUri? TreeUri);
