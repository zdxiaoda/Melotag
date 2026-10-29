using Melotag.Core.Models;

namespace Melotag.Core.Abstractions;

public sealed record TagWriteOptions(
    string Id3v2Version = "2.3",
    bool KeepModifiedTime = true,
    bool WriteLyricsToTag = true,
    bool ReplacePictures = false);

public interface ITagStore
{
    /// <summary>按扩展名判断是否支持（大小写不敏感）。</summary>
    bool CanHandle(string extension);

    /// <summary>读取标签与音频属性；失败抛异常，由调用方计入 Failed。</summary>
    TrackItem Read(IFileHandle file);

    void Write(IFileHandle file, TrackTags tags, TagWriteOptions options);
}
