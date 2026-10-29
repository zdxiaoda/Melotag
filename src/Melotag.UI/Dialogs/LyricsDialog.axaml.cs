namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Melotag.Core.Abstractions;
using Melotag.Core.Lyrics;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.ViewModels;

public partial class LyricsDialog : ModalView
{
    private readonly TagSearchService? _search;
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private readonly List<TagCandidate> _results = [];
    private CancellationTokenSource? _cts;

    public LyricsDialog()
    {
        InitializeComponent();
    }

    public LyricsDialog(TagSearchService search, AppSettings settings, TrackRowViewModel row)
    {
        _search = search;
        _settings = settings;
        _row = row;

        InitializeComponent();

        Editor.Text = row.Lyrics ?? "";
        EncodingCombo.ItemsSource = TextFile.EncodingChoices;
        EncodingCombo.SelectedItem = settings.Lyrics.Encoding;

        Reformat.IsChecked = settings.Lyrics.ReformatTimeTags;
        DeleteBlank.IsChecked = settings.Lyrics.DeleteBlankLines;
        DeleteHead.IsChecked = settings.Lyrics.DeleteHeadTags;
    }

    public string? ResultLyrics { get; private set; }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (_search is null || _settings is null || _row is null) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Progress.IsVisible = true;
        Status.Text = "正在搜索歌词…";
        ResultCombo.ItemsSource = null;
        _results.Clear();

        var query = new TagQuery
        {
            Title = _row.Title,
            Artist = _row.Artist,
            Album = _row.Album,
            FileName = _row.FileName,
            Duration = _row.Info?.Duration,
        };

        try
        {
            var results = await _search.SearchAsync(
                query,
                SourceCapability.Lyrics,
                _settings.Sources.Lyrics,
                _settings.Search.ItemsPerSource,
                _settings.Search.Threads,
                _cts.Token);

            _results.AddRange(results);
            ResultCombo.ItemsSource = results.Select(r => $"{r.SourceId} · {r.Summary}").ToList();
            if (results.Count > 0) ResultCombo.SelectedIndex = 0;
            Status.Text = results.Count == 0 ? "没有找到歌词" : $"共 {results.Count} 条";
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

    private void OnResultChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ResultCombo.SelectedIndex;
        if (index < 0 || index >= _results.Count) return;
        if (string.IsNullOrWhiteSpace(_results[index].LyricsText)) return;
        Editor.Text = _results[index].LyricsText;
    }

    private void OnApplyOps(object? sender, RoutedEventArgs e)
    {
        var text = Editor.Text ?? "";
        if (text.Length == 0) return;

        var doc = LrcDocument.Parse(text);
        if (Reformat.IsChecked == true) doc.ReformatTimeTags();
        if (RemoveTags.IsChecked == true) doc.RemoveTimeTags();
        if (DeleteBlank.IsChecked == true) doc.RemoveBlankLines();
        if (DeleteHead.IsChecked == true) doc.RemoveHeadTags();

        var shift = (double)(ShiftBox.Value ?? 0);
        if (Math.Abs(shift) > 0.001) doc.Shift(TimeSpan.FromSeconds(shift));

        Editor.Text = doc.ToLrcText();
        Message.Text = "已应用处理";
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 LRC",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("歌词") { Patterns = ["*.lrc", "*.txt"] }],
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        var text = TextFile.ReadAllText(stream, out var encoding);
        Editor.Text = text;
        Message.Text = $"已导入（编码 {encoding}）";
    }

    private async void OnSaveLrc(object? sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var suggested = (_row?.FileName ?? "lyrics") + ".lrc";
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 LRC",
            SuggestedFileName = suggested,
            DefaultExtension = "lrc",
            FileTypeChoices = [new FilePickerFileType("歌词") { Patterns = ["*.lrc"] }],
        });

        if (file is null) return;

        var encoding = EncodingCombo.SelectedItem as string ?? _settings.Lyrics.Encoding;
        await using var stream = await file.OpenWriteAsync();
        TextFile.WriteAllText(stream, Editor.Text ?? "", encoding);
        Message.Text = $"已保存：{file.Name}（{encoding}）";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ResultLyrics = Editor.Text;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(false);
    }
}
