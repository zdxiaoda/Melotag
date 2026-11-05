namespace Melotag.UI.ViewModels;

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.UI.Localization;

/// <summary>列表行 + 编辑面板的可绑定模型。数字字段用文本承载，避免输入过程中的绑定异常。</summary>
public partial class TrackRowViewModel : ObservableObject, IDisposable
{
    private const int DetailCoverWidth = 400;
    private const int ThumbnailWidth = 64;

    /// <summary>仅保存用户待写入文件的封面改动；磁盘上的封面在需要时按需读取。</summary>
    private readonly List<TagPicture> _pictures = [];
    private readonly Func<IFileHandle, IReadOnlyList<TagPicture>>? _pictureLoader;
    private AudioInfo? _info;

    private Bitmap? _thumbnail;
    private bool _thumbnailRequested;
    private int _thumbnailGeneration;
    private bool _previewRequested;
    private int _previewGeneration;

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

    public TrackRowViewModel(
        TrackItem item,
        Action<TrackRowViewModel>? remove = null,
        Func<IFileHandle, IReadOnlyList<TagPicture>>? pictureLoader = null)
    {
        _pictureLoader = pictureLoader;
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

    /// <summary>用户待写入的封面改动；空表示保持文件里的原封面。</summary>
    public IReadOnlyList<TagPicture> Pictures => _pictures;
    public bool PicturesDirty { get; private set; }
    public bool HasCover { get; private set; }

    /// <summary>
    /// 移动端列表用的 46px 缩略图。首次被绑定读取时才从文件解码，桌面端没有绑定不会触发。
    /// </summary>
    public Bitmap? CoverThumbnail
    {
        get
        {
            if (!_thumbnailRequested && HasCover)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }

            return _thumbnail;
        }
    }

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

        // 待写入的封面在保存后已落盘，不再保留字节；需要时从文件回读。
        _pictures.Clear();
        PicturesDirty = false;
        HasCover = item.HasCover || item.Tags.Pictures.Count > 0;
        InvalidateCovers();
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
        HasCover = true;
        InvalidateCovers();
        Status = TrackStatus.Modified;
    }

    public void ClearCover()
    {
        _pictures.Clear();
        PicturesDirty = true;
        HasCover = false;
        InvalidateCovers();
        Status = TrackStatus.Modified;
    }

    /// <summary>按需读取当前封面：有未保存改动时用改动，否则回读文件。返回临时列表，不常驻内存。</summary>
    public IReadOnlyList<TagPicture> LoadPictures()
    {
        if (PicturesDirty) return _pictures;
        if (_pictureLoader is null || Source is null) return [];

        try
        {
            return _pictureLoader(Source);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>详情面板用的大图；由列表选中项变化驱动，同一时刻只保留一张。</summary>
    public void RequestCoverPreview()
    {
        _previewRequested = true;
        _ = LoadPreviewAsync();
    }

    public void ReleaseCoverPreview()
    {
        _previewRequested = false;
        _previewGeneration++;
        ReleasePreview();
    }

    public void Dispose()
    {
        _thumbnailGeneration++;
        _previewGeneration++;
        _thumbnail?.Dispose();
        _thumbnail = null;
        ReleasePreview();
    }

    /// <summary>先把绑定切到 null，再释放旧位图，避免界面短时间持有已释放的图像。</summary>
    private void ReleasePreview()
    {
        var previous = CoverPreview;
        CoverPreview = null;
        previous?.Dispose();
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

    /// <summary>封面变化后丢掉已解码的位图；详情预览按原请求状态重新加载。</summary>
    private void InvalidateCovers()
    {
        _thumbnailGeneration++;
        _thumbnail?.Dispose();
        _thumbnail = null;
        _thumbnailRequested = false;
        OnPropertyChanged(nameof(CoverThumbnail));
        OnPropertyChanged(nameof(HasCover));

        if (_previewRequested)
        {
            RequestCoverPreview();
        }
        else
        {
            _previewGeneration++;
            ReleasePreview();
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var generation = ++_thumbnailGeneration;
        var bitmap = await DecodeCoverAsync(ThumbnailWidth);

        if (generation != _thumbnailGeneration)
        {
            bitmap?.Dispose();
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
            AssignThumbnail(generation, bitmap);
        else
            Dispatcher.UIThread.Post(() => AssignThumbnail(generation, bitmap));
    }

    private void AssignThumbnail(int generation, Bitmap? bitmap)
    {
        if (generation != _thumbnailGeneration)
        {
            bitmap?.Dispose();
            return;
        }

        _thumbnail?.Dispose();
        _thumbnail = bitmap;
        OnPropertyChanged(nameof(CoverThumbnail));
    }

    private async Task LoadPreviewAsync()
    {
        var generation = ++_previewGeneration;
        var bitmap = await DecodeCoverAsync(DetailCoverWidth);

        if (generation != _previewGeneration)
        {
            bitmap?.Dispose();
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
            AssignPreview(generation, bitmap);
        else
            Dispatcher.UIThread.Post(() => AssignPreview(generation, bitmap));
    }

    private void AssignPreview(int generation, Bitmap? bitmap)
    {
        if (generation != _previewGeneration)
        {
            bitmap?.Dispose();
            return;
        }

        var previous = CoverPreview;
        CoverPreview = bitmap;
        previous?.Dispose();
    }

    private async Task<Bitmap?> DecodeCoverAsync(int maxWidth)
    {
        if (PicturesDirty)
        {
            if (_pictures.Count == 0) return null;
            var data = _pictures[0].Data;
            return await Task.Run(() => Decode(data, maxWidth));
        }

        if (!HasCover || _pictureLoader is null || Source is null) return null;

        var loader = _pictureLoader;
        var handle = Source;
        try
        {
            return await Task.Run(() =>
            {
                var picture = loader(handle).FirstOrDefault();
                return picture is null ? null : Decode(picture.Data, maxWidth);
            });
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? Decode(byte[] data, int maxWidth)
    {
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            return Bitmap.DecodeToWidth(ms, maxWidth, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            return null;
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
