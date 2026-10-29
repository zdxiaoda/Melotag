namespace Melotag.Core.Models;

/// <summary>一首歌的全部标签字段。空字符串统一归一化为 null。</summary>
public sealed class TrackTags
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtist { get; set; }
    public uint? Year { get; set; }
    public uint? Track { get; set; }
    public uint? Disc { get; set; }
    public string? Genre { get; set; }
    public string? Comment { get; set; }
    public string? Composer { get; set; }
    public string? Lyricist { get; set; }
    public string? Lyrics { get; set; }
    public List<TagPicture> Pictures { get; set; } = [];

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

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
