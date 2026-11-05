namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Melotag.Core.Services;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class CoverDialog : ModalView
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
        MaxSizeBox.IsEnabled = !settings.Pictures.KeepOriginal;

        var pictures = row.LoadPictures();
        if (pictures.Count > 0)
            ShowPreview(pictures[0].Data, pictures[0].MimeType);
    }

    public byte[]? Result { get; private set; }
    public string ResultMime { get; private set; } = "image/jpeg";
    public bool Removed { get; private set; }

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private void ShowPreview(byte[] data, string mime)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new Bitmap(ms);
            (Preview.Source as Bitmap)?.Dispose();
            Preview.Source = bitmap;
            Placeholder.IsVisible = false;
            _current = data;
            _mime = mime;
            Result = data;
            ResultMime = mime;
            Status.Text = $"{data.Length / 1024} KB · {mime}";
        }
        catch (Exception ex)
        {
            Status.Text = Localizer.F("Cover.CannotShow", ex.Message);
        }
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

        var normalized = _settings.Pictures.KeepOriginal
            ? _coverService.ValidateOriginal(bytes, _settings.Pictures.AllowedFormats)
            : _coverService.Normalize(
                bytes,
                _settings.Pictures.MaxWidth,
                _settings.Pictures.MaxHeight,
                (int)(MaxSizeBox.Value ?? _settings.Pictures.MaxSizeKb),
                _settings.Pictures.AllowedFormats);

        if (normalized is null)
        {
            Status.Text = Localizer.T("Cover.Invalid");
            return;
        }

        ShowPreview(normalized.Data, normalized.MimeType);
        Status.Text = $"{normalized.Width}×{normalized.Height} · {normalized.Data.Length / 1024} KB";
        Removed = false;
    }

    private async void OnPickLocal(object? sender, RoutedEventArgs e)
    {
        if (Storage is not { } storage) return;

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
        await stream.CopyToAsync(buffer);
        ApplyWithLimits(buffer.ToArray());
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
        Preview.Source = null;
        Placeholder.IsVisible = true;
        _current = null;
        Result = null;
        Removed = true;
        Status.Text = Localizer.T("Cover.RemoveOnSave");
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
