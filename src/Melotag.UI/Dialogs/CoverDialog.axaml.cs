namespace Melotag.UI.Dialogs;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Melotag.Core.Models;
using Melotag.Core.Services;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;
using Melotag.UI.Media;
using Melotag.UI.ViewModels;

public partial class CoverDialog : ModalView
{
    /// <summary>预览只保留这个尺寸以内的位图，避免打开对话框时解码整张原图。</summary>
    private const int PreviewMaxWidth = 512;
    private const int PreviewMaxHeight = 512;

    /// <summary>本地封面上限；流式读取，超过后不再继续读入或解码。</summary>
    private const int MaxLocalCoverBytes = 32 * 1024 * 1024;

    private readonly TagSearchService? _search;
    private readonly CoverService _coverService = new();
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private readonly List<TagCandidate> _results = [];
    private CancellationTokenSource? _cts;
    private byte[]? _current;
    private string _mime = "image/jpeg";
    private Bitmap? _previewBitmap;
    private int _previewVersion;
    private bool _dirty;
    private bool _initialLoadStarted;
    private bool _detached;

    public CoverDialog()
    {
        InitializeComponent();
    }

    public CoverDialog(TagSearchService search, AppSettings settings, TrackRowViewModel row)
    {
        _search = search;
        _settings = settings;
        _row = row;

        InitializeComponent();

        MaxSizeBox.Value = settings.Pictures.MaxSizeKb;
        MaxSizeBox.IsEnabled = !settings.Pictures.KeepOriginal;
    }

    public byte[]? Result { get; private set; }
    public string ResultMime { get; private set; } = "image/jpeg";
    public bool Removed { get; private set; }

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_initialLoadStarted || _row is null) return;
        _initialLoadStarted = true;
        _ = LoadInitialPreviewAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _detached = true;
        _previewVersion++;
        _cts?.Cancel();
        _cts = null;
        DisposePreview();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task LoadInitialPreviewAsync()
    {
        var row = _row;
        if (row is null) return;

        var version = ++_previewVersion;

        IReadOnlyList<TagPicture> pictures;
        try
        {
            pictures = await Task.Run(row.LoadPictures);
        }
        catch (Exception)
        {
            return;
        }

        if (version != _previewVersion || _detached || pictures.Count == 0) return;

        // 初始预览只是展示文件里已有的封面；没有修改时 OK 不应该让调用方重新 SetCover。
        await ApplyPreviewAsync(pictures[0].Data, pictures[0].MimeType, userChange: false, version);
    }

    private async Task ApplyWithLimitsAsync(byte[] bytes)
    {
        var settings = _settings;
        if (settings is null || _detached) return;

        var version = ++_previewVersion;
        var keepOriginal = settings.Pictures.KeepOriginal;
        var maxSizeKb = (int)(MaxSizeBox.Value ?? settings.Pictures.MaxSizeKb);

        var normalized = await Task.Run(() => keepOriginal
            ? _coverService.ValidateOriginal(bytes, settings.Pictures.AllowedFormats)
            : _coverService.Normalize(
                bytes,
                settings.Pictures.MaxWidth,
                settings.Pictures.MaxHeight,
                maxSizeKb,
                settings.Pictures.AllowedFormats));

        if (version != _previewVersion || _detached) return;

        if (normalized is null)
        {
            Status.Text = Localizer.T("Cover.Invalid");
            return;
        }

        if (!await ApplyPreviewAsync(normalized.Data, normalized.MimeType, userChange: true, version))
            return;

        Status.Text = $"{normalized.Width}×{normalized.Height} · {normalized.Data.Length / 1024} KB";
    }

    private async Task<bool> ApplyPreviewAsync(byte[] data, string mime, bool userChange, int version)
    {
        var bitmap = await Task.Run(() => CoverPreviewDecoder.Decode(data, PreviewMaxWidth, PreviewMaxHeight));

        if (version != _previewVersion || _detached)
        {
            bitmap?.Dispose();
            return false;
        }

        if (bitmap is null)
        {
            Status.Text = Localizer.T("Cover.Invalid");
            return false;
        }

        SetPreviewBitmap(bitmap);
        Placeholder.IsVisible = false;
        _current = data;
        _mime = mime;

        if (userChange)
        {
            _dirty = true;
            Removed = false;
        }
        else
        {
            _dirty = false;
        }

        Status.Text = $"{bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} · {data.Length / 1024} KB";
        return true;
    }

    private void SetPreviewBitmap(Bitmap bitmap)
    {
        var previous = _previewBitmap;
        _previewBitmap = bitmap;
        Preview.Source = bitmap;
        previous?.Dispose();
    }

    private void DisposePreview()
    {
        Preview.Source = null;
        var previous = _previewBitmap;
        _previewBitmap = null;
        _current = null;
        previous?.Dispose();
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (_search is null || _settings is null || _row is null) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Progress.IsVisible = true;
        Status.Text = Localizer.T("Search.CoverSearching");
        ResultCombo.ItemsSource = null;
        _results.Clear();

        var query = new TagQuery
        {
            Title = _row.Title,
            Artist = _row.Artist,
            Album = _row.Album,
            FileName = _row.FileName,
        };

        try
        {
            var results = await _search.SearchAsync(
                query,
                SourceCapability.Cover,
                _settings.Sources.Pictures,
                _settings.Search.ItemsPerSource,
                _settings.Search.Threads,
                _cts.Token);

            _results.AddRange(results.Where(r => !string.IsNullOrWhiteSpace(r.CoverUrl)));
            ResultCombo.ItemsSource = _results.Select(r => $"{r.SourceId} · {r.Summary}").ToList();
            if (_results.Count > 0) ResultCombo.SelectedIndex = 0;
            Status.Text = _results.Count == 0
                ? Localizer.T("Search.NoCoverCandidates")
                : Localizer.F("Search.CandidateCount", _results.Count);
        }
        catch (OperationCanceledException)
        {
            Status.Text = Localizer.T("Search.Cancelled");
        }
        catch (Exception ex)
        {
            Status.Text = Localizer.F("Search.Failed", ex.Message);
        }
        finally
        {
            Progress.IsVisible = false;
        }
    }

    private async void OnResultChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ResultCombo.SelectedIndex;
        if (index < 0 || index >= _results.Count || _search is null) return;

        var candidate = _results[index];
        if (string.IsNullOrWhiteSpace(candidate.CoverUrl)) return;

        var version = ++_previewVersion;
        Progress.IsVisible = true;
        Status.Text = Localizer.T("Search.CoverDownloading");
        try
        {
            var source = _search.Find(candidate.SourceId);
            var bytes = source is null ? null : await source.DownloadCoverAsync(candidate.CoverUrl, CancellationToken.None);
            if (bytes is null)
            {
                Status.Text = Localizer.T("Search.CoverDownloadFailed");
                return;
            }

            if (version != _previewVersion || _detached) return;
            await ApplyWithLimitsAsync(bytes);
        }
        finally
        {
            Progress.IsVisible = false;
        }
    }

    private async void OnPickLocal(object? sender, RoutedEventArgs e)
    {
        if (Storage is not { } storage) return;

        var version = _previewVersion;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("Cover.PickTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.T("Cover.ImageType")) { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp"] },
            ],
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var buffer = new MemoryStream();
        if (!await CopyWithLimitAsync(stream, buffer, MaxLocalCoverBytes))
        {
            Status.Text = Localizer.T("Cover.Invalid");
            return;
        }

        if (version != _previewVersion || _detached) return;
        await ApplyWithLimitsAsync(buffer.ToArray());
    }

    private static async Task<bool> CopyWithLimitAsync(Stream source, Stream destination, int maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) return true;

            total += read;
            if (total > maxBytes) return false;

            await destination.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            Status.Text = Localizer.T("Cover.NoExport");
            return;
        }

        if (Storage is not { } storage) return;

        var ext = _mime.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.T("Cover.ExportTitle"),
            SuggestedFileName = (_row?.FileName ?? "cover") + "." + ext,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(Localizer.T("Cover.ImageType")) { Patterns = [$"*.{ext}"] }],
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(_current);
        Status.Text = Localizer.F("Cover.Exported", file.Name);
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        _previewVersion++;
        DisposePreview();
        Placeholder.IsVisible = true;
        _dirty = true;
        Removed = true;
        Result = null;
        Status.Text = Localizer.T("Cover.RemoveOnSave");
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        // 只有在用户确实改过封面时才把数据交回调用方；未修改时返回 null，避免无意义的 SetCover。
        Result = _dirty && !Removed ? _current : null;
        ResultMime = _mime;
        Close(true);
        SchedulePreviewDispose();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(false);
        SchedulePreviewDispose();
    }

    /// <summary>桌面窗口关闭/Android 浮层移除时兜底释放预览位图。</summary>
    private void SchedulePreviewDispose()
    {
        _detached = true;
        _previewVersion++;

        // 让关闭流程先完成绑定解绑，再释放位图，避免界面短暂引用已释放图像。
        Dispatcher.UIThread.Post(DisposePreview, DispatcherPriority.Background);
    }
}
