namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Melotag.Core.Services;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.ViewModels;

public partial class CoverDialog : Window
{
    private readonly TagSearchService? _search;
    private readonly CoverService _coverService = new();
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private readonly List<TagCandidate> _results = [];
    private CancellationTokenSource? _cts;
    private byte[]? _current;
    private string _mime = "image/jpeg";

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

        if (row.Pictures.Count > 0)
            ShowPreview(row.Pictures[0].Data, row.Pictures[0].MimeType);
    }

    public byte[]? Result { get; private set; }
    public string ResultMime { get; private set; } = "image/jpeg";
    public bool Removed { get; private set; }

    private void ShowPreview(byte[] data, string mime)
    {
        try
        {
            using var ms = new MemoryStream(data);
            Preview.Source = new Bitmap(ms);
            Placeholder.IsVisible = false;
            _current = data;
            _mime = mime;
            Result = data;
            ResultMime = mime;
            Status.Text = $"{data.Length / 1024} KB · {mime}";
        }
        catch (Exception ex)
        {
            Status.Text = "无法显示该图片：" + ex.Message;
        }
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (_search is null || _settings is null || _row is null) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Progress.IsVisible = true;
        Status.Text = "正在搜索封面…";
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
            Status.Text = _results.Count == 0 ? "没有可用的封面候选" : $"共 {_results.Count} 条候选";
        }
        catch (OperationCanceledException)
        {
            Status.Text = "已取消";
        }
        catch (Exception ex)
        {
            Status.Text = "搜索失败：" + ex.Message;
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

        Progress.IsVisible = true;
        Status.Text = "正在下载封面…";
        try
        {
            var source = _search.Find(candidate.SourceId);
            var bytes = source is null ? null : await source.DownloadCoverAsync(candidate.CoverUrl, CancellationToken.None);
            if (bytes is null)
            {
                Status.Text = "下载失败";
                return;
            }

            ApplyWithLimits(bytes);
        }
        finally
        {
            Progress.IsVisible = false;
        }
    }

    private void ApplyWithLimits(byte[] bytes)
    {
        if (_settings is null) return;

        var normalized = _coverService.Normalize(
            bytes,
            _settings.Pictures.MaxWidth,
            _settings.Pictures.MaxHeight,
            (int)(MaxSizeBox.Value ?? _settings.Pictures.MaxSizeKb),
            _settings.Pictures.AllowedFormats);

        if (normalized is null)
        {
            Status.Text = "图片格式或体积不符合设置（可在设置里调整上限）";
            return;
        }

        ShowPreview(normalized.Data, normalized.MimeType);
        Status.Text = $"{normalized.Width}×{normalized.Height} · {normalized.Data.Length / 1024} KB";
        Removed = false;
    }

    private async void OnPickLocal(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择封面图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp"] },
            ],
        });

        var path = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is null) return;

        ApplyWithLimits(await File.ReadAllBytesAsync(path));
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            Status.Text = "当前没有封面可导出";
            return;
        }

        var ext = _mime.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出封面",
            SuggestedFileName = (_row?.FileName ?? "cover") + "." + ext,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType("图片") { Patterns = [$"*.{ext}"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is null) return;

        await File.WriteAllBytesAsync(path, _current);
        Status.Text = "已导出：" + Path.GetFileName(path);
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        Preview.Source = null;
        Placeholder.IsVisible = true;
        _current = null;
        Result = null;
        Removed = true;
        Status.Text = "保存时将移除封面";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = _current;
        ResultMime = _mime;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(false);
    }
}
