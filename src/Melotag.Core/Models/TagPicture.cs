namespace Melotag.Core.Models;

/// <summary>与 ID3v2 APIC 图片类型编号对齐，便于跨容器映射。</summary>
public enum PictureKind
{
    Other = 0,
    FrontCover = 3,
    BackCover = 4,
}

/// <summary>
/// 一张封面图。
/// 注意（有意保持现状，不改模型）：
/// 1. record 的自动相等性对 Data 走数组引用比较，不是逐字节比较；
///    两张内容相同但数组不同的封面不会相等，需要时请自行按内容比较。
/// 2. <see cref="TrackTags.Clone"/> 只复制列表，不复制字节数组，克隆体与原对象共享同一份 Data。
/// 因此调用方插入/读取封面后不应就地修改 Data。
/// </summary>
public sealed record TagPicture(
    byte[] Data,
    string MimeType,
    PictureKind Kind = PictureKind.FrontCover,
    string? Description = null)
{
    /// <summary>Data 可能来自反序列化/反射构造而为 null，这里不能直接取 Length。</summary>
    public override string ToString() => $"{Kind} {MimeType} {Data?.Length ?? 0}B";
}
