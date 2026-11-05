namespace Melotag.UI.ViewModels;

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.UI.Localization;
using Melotag.UI.Media;

/// <summary>列表行 + 编辑面板的可绑定模型。数字字段用文本承载，避免输入过程中的绑定异常。</summary>
public partial class TrackRowViewModel : ObservableObject, IDisposable
{
    private const int DetailCoverWidth = 400;
    private const int DetailCoverHeight = 560;
    private const int ThumbnailWidth = 64;
    private const int ThumbnailHeight = 64;

    /// <summary>仅保存用户待写入文件的封面改动；磁盘上的封面在需要时按需读取。</summary>
    private readonly List<TagPicture> _pictures = [];
    private readonly Func<IFileHandle, IReadOnlyList<TagPicture>>? _pictureLoader;
    private AudioInfo? _info;
    private DateTime? _modifiedUtc;
    private DateTime? _createdUtc;

    // 批量路径（自动匹配等）可以把待写入封面暂存到磁盘，行内只保留路径，避免整库原图常驻内存。
    private string? _spooledCoverPath;
    private string? _spooledCoverMime;

    // 缩略图不常驻强引用：命中 ThumbnailCache（有界 LRU）或 UI 仍持有弱引用时才返回。
    private WeakReference<Bitmap>? _thumbnail;
    private bool _thumbnailAttemptStarted;
    private bool _thumbnailAttemptCompleted;
    private bool _thumbnailFailed;
    private int _thumbnailGeneration;
    private bool _previewRequested;
    private int _previewGeneration;
    private bool _disposed;

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

    /// <summary>内存中的待写入封面改动；磁盘暂存时为空，读取请用 <see cref="LoadPictures"/>。</summary>
    public IReadOnlyList<TagPicture> Pictures => _pictures;
    public bool PicturesDirty { get; private set; }
    public bool HasCover { get; private set; }

    /// <summary>
    /// 移动端列表用的 46px 缩略图。首次被绑定读取时才从文件解码，桌面端没有绑定不会触发。
    /// 缩略图放在有界 LRU 缓存中；缓存淘汰后由 GC 回收，不会随滚动永久累积。
    /// </summary>
    public Bitmap? CoverThumbnail
    {
        get
        {
            if (_disposed || !HasCover) return null;

            if (ThumbnailCache.TryGet(Key, out var cached))
                return cached;

            if (_thumbnail is { } reference && reference.TryGetTarget(out var bitmap))
            {
                // 缓存可能刚淘汰了它，但界面仍持有强引用；重新放回缓存。
                ThumbnailCache.Put(Key, bitmap);
                return bitmap;
            }

            // 第一次加载，或上次成功后缩略图已被淘汰（滚动回来时重新解码）。
            if (!_thumbnailAttemptStarted || (_thumbnailAttemptCompleted && !_thumbnailFailed))
            {
                _thumbnailAttemptStarted = true;
                _thumbnailAttemptCompleted = false;
                _thumbnailFailed = false;
                _ = LoadThumbnailAsync();
            }

            return null;
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

    public long SizeBytes => _info?.SizeBytes ?? Source?.Length ?? 0;
    public int? BitrateKbps => _info?.BitrateKbps;
    public int? SampleRateHz => _info?.SampleRateHz;
    public int? Channels => _info?.Channels;
    public DateTime? ModifiedUtc => _modifiedUtc;
    public DateTime? CreatedUtc => _createdUtc;

    public string BitrateText => _info?.BitrateText ?? "";
    public string SampleRateText => _info?.SampleRateText ?? "";
    public string ChannelsText => _info?.ChannelsText ?? "";
    public string ModifiedText => FormatTime(_modifiedUtc);
    public string CreatedText => FormatTime(_createdUtc);

    private static string FormatTime(DateTime? utc) =>
        utc is null ? "" : utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

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
        _modifiedUtc = item.ModifiedUtc;
        _createdUtc = item.CreatedUtc;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(BitrateKbps));
        OnPropertyChanged(nameof(SampleRateHz));
        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(BitrateText));
        OnPropertyChanged(nameof(SampleRateText));
        OnPropertyChanged(nameof(ChannelsText));
        OnPropertyChanged(nameof(ModifiedUtc));
        OnPropertyChanged(nameof(CreatedUtc));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(CreatedText));
        DurationText = item.Info?.DurationText ?? "";
        FormatText = item.Info?.Format ?? "";
        SizeText = item.Info?.SizeText ?? "";
        Status = item.Status;
        ErrorText = item.Error;

        // 待写入的封面在保存后已落盘（或本次重新读取要丢弃改动），不再保留字节/暂存文件。
        ClearSpooledCover();
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

    /// <summary>
    /// 设置待写入封面。<paramref name="spoolToDisk"/> 为 true 时把字节写入临时文件，
    /// 行内只保留路径；适合批量自动匹配等一次写入很多行的场景。
    /// 返回 false 表示磁盘暂存失败（此时不会改内存/封面状态），批量调用方应计入 miss，
    /// 不要把整批原图退回内存常驻。
    /// </summary>
    public bool SetCover(byte[] data, string mimeType, bool spoolToDisk = false)
    {
        string? spooledPath = null;
        if (spoolToDisk)
        {
            spooledPath = CoverSpool.TryStore(data);
            if (spooledPath is null) return false;
        }

        var previousSpool = _spooledCoverPath;
        _spooledCoverPath = spooledPath;
        _spooledCoverMime = spooledPath is null ? null : mimeType;
        _pictures.Clear();
        if (spooledPath is null)
            _pictures.Add(new TagPicture(data, mimeType));

        CoverSpool.TryDelete(previousSpool);
        PicturesDirty = true;
        HasCover = true;
        InvalidateCovers();
        Status = TrackStatus.Modified;
        return true;
    }

    public void ClearCover()
    {
        ClearSpooledCover();
        _pictures.Clear();
        PicturesDirty = true;
        HasCover = false;
        InvalidateCovers();
        Status = TrackStatus.Modified;
    }

    /// <summary>按需读取当前封面：有未保存改动时用改动，否则回读文件。返回临时列表，不常驻内存。</summary>
    public IReadOnlyList<TagPicture> LoadPictures()
    {
        if (PicturesDirty)
        {
            if (_spooledCoverPath is null) return _pictures;
            return TryReadSpooledCover() is { } picture ? [picture] : [];
        }

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
        if (_disposed) return;
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
        if (_disposed) return;
        _disposed = true;
        _previewGeneration++;
        ReleaseThumbnail();
        ReleasePreview();
        ClearSpooledCover();
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
        var previousKey = Key;
        Source = newHandle;
        Key = newHandle.Key;
        Path = newHandle.DisplayPath;
        FileName = newHandle.FileName;

        // 缩略图仍有效，只是缓存键（文件标识）变了。
        if (!string.Equals(previousKey, Key, StringComparison.Ordinal) &&
            ThumbnailCache.Remove(previousKey) is { } cached)
        {
            ThumbnailCache.Put(Key, cached);
        }
    }

    /// <summary>
    /// 生成待写入的标签。磁盘暂存的封面会在这里逐首读回（一次只在内存里保留一首的字节），
    /// 调用方（保存流程）随后逐首写文件即可。
    /// </summary>
    public TrackTags ToTags(bool includePictures = true) => new()
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
        Pictures = includePictures ? ReadPicturesForSave() : [],
    };

    private static uint? ParseUInt(string? text) =>
        uint.TryParse(text?.Trim(), out var value) ? value : null;

    /// <summary>封面变化后丢掉已解码的位图；详情预览按原请求状态重新加载。</summary>
    private void InvalidateCovers()
    {
        ReleaseThumbnail();
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

    /// <summary>释放缩略图强引用/缓存并让迟到的解码结果作废。</summary>
    private void ReleaseThumbnail()
    {
        _thumbnailAttemptStarted = false;
        _thumbnailAttemptCompleted = false;
        _thumbnailFailed = false;
        _thumbnailGeneration++;

        var cached = ThumbnailCache.Remove(Key);
        _thumbnail = null;

        // 先让绑定刷新到 null，再释放缓存里的位图，避免界面短暂引用已释放图像。
        OnPropertyChanged(nameof(CoverThumbnail));
        cached?.Dispose();
    }

    private async Task LoadThumbnailAsync()
    {
        var generation = ++_thumbnailGeneration;
        var bitmap = await DecodeCoverAsync(ThumbnailWidth, ThumbnailHeight);

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

        _thumbnailAttemptCompleted = true;
        _thumbnailFailed = bitmap is null;

        if (bitmap is null) return;

        _thumbnail = new WeakReference<Bitmap>(bitmap);
        ThumbnailCache.Put(Key, bitmap);
        OnPropertyChanged(nameof(CoverThumbnail));
    }

    private async Task LoadPreviewAsync()
    {
        var generation = ++_previewGeneration;
        var bitmap = await DecodeCoverAsync(DetailCoverWidth, DetailCoverHeight);

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

    private async Task<Bitmap?> DecodeCoverAsync(int maxWidth, int maxHeight)
    {
        if (PicturesDirty)
        {
            var spooledPath = _spooledCoverPath;
            var spooledMime = _spooledCoverMime;
            if (spooledPath is not null)
            {
                return await Task.Run(() => TryReadSpooledCover(spooledPath, spooledMime) is { } picture
                    ? Decode(picture.Data, maxWidth, maxHeight)
                    : null);
            }

            if (_pictures.Count == 0) return null;
            var data = _pictures[0].Data;
            return await Task.Run(() => Decode(data, maxWidth, maxHeight));
        }

        if (!HasCover || _pictureLoader is null || Source is null) return null;

        var loader = _pictureLoader;
        var handle = Source;
        try
        {
            return await Task.Run(() =>
            {
                var picture = loader(handle).FirstOrDefault();
                return picture is null ? null : Decode(picture.Data, maxWidth, maxHeight);
            });
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? Decode(byte[] data, int maxWidth, int maxHeight) =>
        CoverPreviewDecoder.Decode(data, maxWidth, maxHeight);

    /// <summary>保存前取待写入封面；暂存文件读不出来时退化为“不替换封面”，避免删掉文件里的原封面。</summary>
    private List<TagPicture> ReadPicturesForSave()
    {
        if (_spooledCoverPath is null) return [.. _pictures];

        if (TryReadSpooledCover() is { } picture)
            return [picture];

        ClearSpooledCover();
        PicturesDirty = false;
        return [];
    }

    private TagPicture? TryReadSpooledCover()
    {
        var path = _spooledCoverPath;
        return path is null ? null : TryReadSpooledCover(path, _spooledCoverMime);
    }

    private static TagPicture? TryReadSpooledCover(string path, string? mimeType)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length == 0) return null;
            return new TagPicture(data, mimeType ?? "image/jpeg");
        }
        catch
        {
            return null;
        }
    }

    private void ClearSpooledCover()
    {
        var path = _spooledCoverPath;
        _spooledCoverPath = null;
        _spooledCoverMime = null;
        CoverSpool.TryDelete(path);
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
