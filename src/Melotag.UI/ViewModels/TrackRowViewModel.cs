namespace Melotag.UI.ViewModels;

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;

/// <summary>列表行 + 编辑面板的可绑定模型。数字字段用文本承载，避免输入过程中的绑定异常。</summary>
public partial class TrackRowViewModel : ObservableObject
{
    private readonly List<TagPicture> _pictures = [];
    private AudioInfo? _info;

    public TrackRowViewModel(TrackItem item)
    {
        path = item.Path;
        fileName = item.DisplayName;
        Source = item.Source;
        Key = item.Key;
        ApplyFrom(item);
    }

    /// <summary>读取该行时的文件句柄；SAF 下无法从路径重建，保存必须用它。</summary>
    public IFileHandle? Source { get; private set; }

    /// <summary>历史记录 / 去重键。</summary>
    public string Key { get; private set; }

    [ObservableProperty] private string path;
    [ObservableProperty] private string fileName;

    [ObservableProperty] private string? title;
    [ObservableProperty] private string? artist;
    [ObservableProperty] private string? album;
    [ObservableProperty] private string? albumArtist;
    [ObservableProperty] private string? yearText;
    [ObservableProperty] private string? trackText;
    [ObservableProperty] private string? discText;
    [ObservableProperty] private string? genre;
    [ObservableProperty] private string? comment;
    [ObservableProperty] private string? composer;
    [ObservableProperty] private string? lyricist;
    [ObservableProperty] private string? lyrics;

    [ObservableProperty] private string statusText = "待读取";
    [ObservableProperty] private string durationText = "";
    [ObservableProperty] private string formatText = "";
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private Bitmap? coverPreview;

    public AudioInfo? Info => _info;
    public IReadOnlyList<TagPicture> Pictures => _pictures;
    public bool PicturesDirty { get; private set; }
    public bool HasCover => _pictures.Count > 0;

    public void ApplyFrom(TrackItem item)
    {
        if (item.Source is not null)
        {
            Source = item.Source;
            Key = item.Key;
        }

        Title = item.Tags.Title;
        Artist = item.Tags.Artist;
        Album = item.Tags.Album;
        AlbumArtist = item.Tags.AlbumArtist;
        YearText = item.Tags.Year?.ToString();
        TrackText = item.Tags.Track?.ToString();
        DiscText = item.Tags.Disc?.ToString();
        Genre = item.Tags.Genre;
        Comment = item.Tags.Comment;
        Composer = item.Tags.Composer;
        Lyricist = item.Tags.Lyricist;
        Lyrics = item.Tags.Lyrics;

        _info = item.Info;
        DurationText = item.Info?.DurationText ?? "";
        FormatText = item.Info?.Format ?? "";
        SizeText = item.Info?.SizeText ?? "";
        StatusText = Describe(item.Status);
        ErrorText = item.Error;

        _pictures.Clear();
        _pictures.AddRange(item.Tags.Pictures);
        PicturesDirty = false;
        UpdateCoverPreview();
    }

    /// <summary>只更新标签字段（用于在线匹配/正则解析），保留文件与技术信息。</summary>
    public void ApplyTags(TrackTags tags)
    {
        Title = tags.Title;
        Artist = tags.Artist;
        Album = tags.Album;
        AlbumArtist = tags.AlbumArtist;
        YearText = tags.Year?.ToString();
        TrackText = tags.Track?.ToString();
        DiscText = tags.Disc?.ToString();
        Genre = tags.Genre;
        Comment = tags.Comment;
        Composer = tags.Composer;
        Lyricist = tags.Lyricist;
        if (tags.Lyrics is not null) Lyrics = tags.Lyrics;
        StatusText = "已修改";
    }

    public void SetCover(byte[] data, string mimeType)
    {
        _pictures.Clear();
        _pictures.Add(new TagPicture(data, mimeType));
        PicturesDirty = true;
        UpdateCoverPreview();
        StatusText = "已修改";
    }

    public void ClearCover()
    {
        _pictures.Clear();
        PicturesDirty = true;
        CoverPreview = null;
        StatusText = "已修改";
    }

    public void RenameTo(string newPath)
    {
        Path = newPath;
        FileName = System.IO.Path.GetFileName(newPath);
        Key = newPath;
        Source = null;
    }

    public TrackTags ToTags() => new()
    {
        Title = TrackTags.Normalize(Title),
        Artist = TrackTags.Normalize(Artist),
        Album = TrackTags.Normalize(Album),
        AlbumArtist = TrackTags.Normalize(AlbumArtist),
        Year = ParseUInt(YearText),
        Track = ParseUInt(TrackText),
        Disc = ParseUInt(DiscText),
        Genre = TrackTags.Normalize(Genre),
        Comment = TrackTags.Normalize(Comment),
        Composer = TrackTags.Normalize(Composer),
        Lyricist = TrackTags.Normalize(Lyricist),
        Lyrics = string.IsNullOrWhiteSpace(Lyrics) ? null : Lyrics,
        Pictures = [.. _pictures],
    };

    private static string Describe(TrackStatus status) => status switch
    {
        TrackStatus.Loaded => "已读取",
        TrackStatus.Saved => "已保存",
        TrackStatus.Modified => "已修改",
        TrackStatus.Failed => "失败",
        TrackStatus.Skipped => "跳过",
        _ => "待读取",
    };

    private static uint? ParseUInt(string? text) =>
        uint.TryParse(text?.Trim(), out var value) ? value : null;

    private void UpdateCoverPreview()
    {
        if (_pictures.Count == 0)
        {
            CoverPreview = null;
            return;
        }

        try
        {
            using var ms = new MemoryStream(_pictures[0].Data);
            CoverPreview = new Bitmap(ms);
        }
        catch
        {
            CoverPreview = null;
        }
    }

    partial void OnTitleChanged(string? value) => MarkModified();
    partial void OnArtistChanged(string? value) => MarkModified();
    partial void OnAlbumChanged(string? value) => MarkModified();
    partial void OnAlbumArtistChanged(string? value) => MarkModified();
    partial void OnYearTextChanged(string? value) => MarkModified();
    partial void OnTrackTextChanged(string? value) => MarkModified();
    partial void OnDiscTextChanged(string? value) => MarkModified();
    partial void OnGenreChanged(string? value) => MarkModified();
    partial void OnCommentChanged(string? value) => MarkModified();
    partial void OnComposerChanged(string? value) => MarkModified();
    partial void OnLyricistChanged(string? value) => MarkModified();
    partial void OnLyricsChanged(string? value) => MarkModified();

    private void MarkModified()
    {
        if (StatusText is "已读取" or "已保存" or "待读取")
            StatusText = "已修改";
    }
}
