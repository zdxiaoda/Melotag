namespace Melotag.Core.Models;

/// <summary>与 ID3v2 APIC 图片类型编号对齐，便于跨容器映射。</summary>
public enum PictureKind
{
    Other = 0,
    FrontCover = 3,
    BackCover = 4,
}

public sealed record TagPicture(
    byte[] Data,
    string MimeType,
    PictureKind Kind = PictureKind.FrontCover,
    string? Description = null)
{
    public override string ToString() => $"{Kind} {MimeType} {Data.Length}B";
}
