namespace Melotag.Core.Models;

/// <summary>
/// 一首歌的全部标签字段。空字符串统一归一化为 null：
/// 通过 setter 写入空白字符串会被规整成 null，避免「空串」和「无值」在 UI/合并/写回时表现不一致。
/// 兼容性说明：这只影响写入路径，反序列化历史数据（"" → null）是安全的，JSON 形状不变；
/// setter 不做 Trim，因此有意义的空格/换行内容不会被悄悄改写（需要 Trim 请用 <see cref="Normalize"/>）。
/// </summary>
public sealed class TrackTags
{
    private string? _title;
    private string? _artist;
    private string? _album;
    private string? _albumArtist;
    private string? _genre;
    private string? _comment;
    private string? _composer;
    private string? _lyricist;
    private string? _lyrics;
    private List<TagPicture> _pictures = [];

    public string? Title { get => _title; set => _title = NullIfBlank(value); }
    public string? Artist { get => _artist; set => _artist = NullIfBlank(value); }
    public string? Album { get => _album; set => _album = NullIfBlank(value); }
    public string? AlbumArtist { get => _albumArtist; set => _albumArtist = NullIfBlank(value); }
    public uint? Year { get; set; }
    public uint? Track { get; set; }
    public uint? Disc { get; set; }
    public string? Genre { get => _genre; set => _genre = NullIfBlank(value); }
    public string? Comment { get => _comment; set => _comment = NullIfBlank(value); }
    public string? Composer { get => _composer; set => _composer = NullIfBlank(value); }
    public string? Lyricist { get => _lyricist; set => _lyricist = NullIfBlank(value); }
    public string? Lyrics { get => _lyrics; set => _lyrics = NullIfBlank(value); }

    /// <summary>封面列表。赋 null 视作空列表，与「空串归一化为 null」保持同一种宽容度。</summary>
    public List<TagPicture> Pictures { get => _pictures; set => _pictures = value ?? []; }

    /// <summary>浅克隆：列表重新创建，但 <see cref="TagPicture"/>（含 Data 字节数组）与原件共享。</summary>
    public TrackTags Clone()
    {
        var copy = (TrackTags)MemberwiseClone();
        copy.Pictures = [.. Pictures];
        return copy;
    }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist) &&
        string.IsNullOrWhiteSpace(Album) && string.IsNullOrWhiteSpace(AlbumArtist) &&
        Year is null && Track is null && Disc is null &&
        string.IsNullOrWhiteSpace(Genre) && string.IsNullOrWhiteSpace(Comment) &&
        string.IsNullOrWhiteSpace(Composer) && string.IsNullOrWhiteSpace(Lyricist) &&
        string.IsNullOrWhiteSpace(Lyrics) && Pictures.Count == 0;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
