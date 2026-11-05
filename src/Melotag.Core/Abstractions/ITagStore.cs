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

    /// <summary>
    /// 读取标签与音频属性；失败抛异常，由调用方计入 Failed。
    /// <paramref name="includePictures"/> 为 false 时只记录 <see cref="TrackItem.HasCover"/>，
    /// 不复制图片字节（批量导入用，避免常驻大量封面数据）。
    /// </summary>
    TrackItem Read(IFileHandle file, bool includePictures = true);

    /// <summary>按需读取文件里的全部图片（详情预览、封面对话框、导出封面用）。</summary>
    IReadOnlyList<TagPicture> ReadPictures(IFileHandle file);

    void Write(IFileHandle file, TrackTags tags, TagWriteOptions options);
}
