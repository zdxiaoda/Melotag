namespace Melotag.UI.ViewModels;

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.UI.Localization;

/// <summary>列表行 + 编辑面板的可绑定模型。数字字段用文本承载，避免输入过程中的绑定异常。</summary>
public partial class TrackRowViewModel : ObservableObject
{
    private readonly List<TagPicture> _pictures = [];
    private AudioInfo? _info;

    private string? _savedTitle;
    private string? _savedArtist;
    private string? _savedAlbum;
    private string? _savedAlbumArtist;
    private uint? _savedYear;
    private uint? _savedTrack;
    private uint? _savedDisc;
    private string? _savedGenre;
    private string? _savedComposer;
    private string? _savedComment;
    private string? _savedLyrics;

    public TrackRowViewModel(TrackItem item, Action<TrackRowViewModel>? remove = null)
    {
        path = item.Path;
        fileName = item.DisplayName;
        Source = item.Source;
        Key = item.Key;

        if (remove is not null)
            RemoveCommand = new RelayCommand(() => remove(this));

        ApplyFrom(item);
    }

    /// <summary>行内的"移出列表"命令（移动端列表每行右侧的 ✕）。</summary>
    public IRelayCommand? RemoveCommand { get; }

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

    [ObservableProperty] private TrackStatus status = TrackStatus.Pending;
    [ObservableProperty] private string durationText = "";
    [ObservableProperty] private string formatText = "";
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private Bitmap? coverPreview;

    public AudioInfo? Info => _info;
    public IReadOnlyList<TagPicture> Pictures => _pictures;
    public bool PicturesDirty { get; private set; }
    public bool HasCover => _pictures.Count > 0;

    /// <summary>本地化后的状态文本；逻辑判断一律使用 <see cref="Status"/>。</summary>
    public string StatusText => Localizer.T($"TrackStatus.{Status}");

    /// <summary>编辑面板字段标题；相对上次保存有改动时追加“（已修改）”。</summary>
    public string TitleLabel => FieldLabel("Field.Title", !SameText(Title, _savedTitle));
    public string ArtistLabel => FieldLabel("Field.Artist", !SameText(Artist, _savedArtist));
    public string AlbumLabel => FieldLabel("Field.Album", !SameText(Album, _savedAlbum));
    public string AlbumArtistLabel => FieldLabel("Field.AlbumArtist", !SameText(AlbumArtist, _savedAlbumArtist));
    public string YearLabel => FieldLabel("Field.Year", ParseUInt(YearText) != _savedYear);
    public string TrackLabel => FieldLabel("Field.Track", ParseUInt(TrackText) != _savedTrack);
    public string DiscLabel => FieldLabel("Field.Disc", ParseUInt(DiscText) != _savedDisc);
    public string GenreLabel => FieldLabel("Field.Genre", !SameText(Genre, _savedGenre));
    public string ComposerLabel => FieldLabel("Field.Composer", !SameText(Composer, _savedComposer));
    public string CommentLabel => FieldLabel("Field.Comment", !SameText(Comment, _savedComment));
    public string LyricsLabel => FieldLabel("Field.Lyrics", !SameText(Lyrics, _savedLyrics));

    private static string FieldLabel(string key, bool modified) =>
        modified ? Localizer.T(key) + Localizer.T("Field.ModifiedSuffix") : Localizer.T(key);

    private static bool SameText(string? value, string? saved) =>
        string.Equals(TrackTags.Normalize(value), saved, StringComparison.Ordinal);

    /// <summary>供 DataGrid 按数值排序（显示仍用 *Text）。</summary>
    public uint? TrackNumber => ParseUInt(TrackText);
    public uint? DiscNumber => ParseUInt(DiscText);
    public uint? YearNumber => ParseUInt(YearText);
    public double DurationSeconds => _info is null ? 0 : _info.Duration.TotalSeconds;

    partial void OnStatusChanged(TrackStatus value) => OnPropertyChanged(nameof(StatusText));

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StatusText));
        NotifyFieldLabels();
    }

    public void ApplyFrom(TrackItem item)
    {
        if (item.Source is not null)
        {
            Source = item.Source;
            Key = item.Key;
        }

        _savedTitle = TrackTags.Normalize(item.Tags.Title);
        _savedArtist = TrackTags.Normalize(item.Tags.Artist);
        _savedAlbum = TrackTags.Normalize(item.Tags.Album);
        _savedAlbumArtist = TrackTags.Normalize(item.Tags.AlbumArtist);
        _savedYear = item.Tags.Year;
        _savedTrack = item.Tags.Track;
        _savedDisc = item.Tags.Disc;
        _savedGenre = TrackTags.Normalize(item.Tags.Genre);
        _savedComposer = TrackTags.Normalize(item.Tags.Composer);
        _savedComment = TrackTags.Normalize(item.Tags.Comment);
        _savedLyrics = TrackTags.Normalize(item.Tags.Lyrics);

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
        OnPropertyChanged(nameof(DurationSeconds));
        DurationText = item.Info?.DurationText ?? "";
        FormatText = item.Info?.Format ?? "";
        SizeText = item.Info?.SizeText ?? "";
        Status = item.Status;
        ErrorText = item.Error;

        _pictures.Clear();
        _pictures.AddRange(item.Tags.Pictures);
        PicturesDirty = false;
        UpdateCoverPreview();
        NotifyFieldLabels();
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
        Status = TrackStatus.Modified;
    }

    public void SetCover(byte[] data, string mimeType)
    {
        _pictures.Clear();
        _pictures.Add(new TagPicture(data, mimeType));
        PicturesDirty = true;
        UpdateCoverPreview();
        Status = TrackStatus.Modified;
    }

    public void ClearCover()
    {
        _pictures.Clear();
        PicturesDirty = true;
        CoverPreview = null;
        Status = TrackStatus.Modified;
    }

    public void RenameTo(IFileHandle newHandle)
    {
        Source = newHandle;
        Key = newHandle.Key;
        Path = newHandle.DisplayPath;
        FileName = newHandle.FileName;
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

    partial void OnTitleChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(TitleLabel));
    }

    partial void OnArtistChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(ArtistLabel));
    }

    partial void OnAlbumChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(AlbumLabel));
    }

    partial void OnAlbumArtistChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(AlbumArtistLabel));
    }

    partial void OnYearTextChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(YearNumber));
        OnPropertyChanged(nameof(YearLabel));
    }

    partial void OnTrackTextChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(TrackNumber));
        OnPropertyChanged(nameof(TrackLabel));
    }

    partial void OnDiscTextChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(DiscNumber));
        OnPropertyChanged(nameof(DiscLabel));
    }

    partial void OnGenreChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(GenreLabel));
    }

    partial void OnCommentChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(CommentLabel));
    }

    partial void OnComposerChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(ComposerLabel));
    }

    partial void OnLyricistChanged(string? value) => MarkModified();

    partial void OnLyricsChanged(string? value)
    {
        MarkModified();
        OnPropertyChanged(nameof(LyricsLabel));
    }

    private void NotifyFieldLabels()
    {
        OnPropertyChanged(nameof(TitleLabel));
        OnPropertyChanged(nameof(ArtistLabel));
        OnPropertyChanged(nameof(AlbumLabel));
        OnPropertyChanged(nameof(AlbumArtistLabel));
        OnPropertyChanged(nameof(YearLabel));
        OnPropertyChanged(nameof(TrackLabel));
        OnPropertyChanged(nameof(DiscLabel));
        OnPropertyChanged(nameof(GenreLabel));
        OnPropertyChanged(nameof(ComposerLabel));
        OnPropertyChanged(nameof(CommentLabel));
        OnPropertyChanged(nameof(LyricsLabel));
    }

    private void MarkModified()
    {
        if (Status is TrackStatus.Pending or TrackStatus.Loaded or TrackStatus.Saved)
            Status = TrackStatus.Modified;
    }
}
