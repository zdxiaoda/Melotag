namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Merge;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class TagSearchDialog : ModalView
{
    private readonly TagSearchService? _search;
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private readonly Dictionary<string, string> _sourceNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TagCandidate> _allResults = [];
    private bool _updatingFilter;
    private CancellationTokenSource? _cts;

    private sealed record SourceFilterOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    public TagSearchDialog()
    {
        InitializeComponent();
        HookNarrowMode();
    }

    public TagSearchDialog(TagSearchService search, AppSettings settings, TrackRowViewModel row,
        IReadOnlyList<ITagSource>? sources = null)
    {
        _search = search;
        _settings = settings;
        _row = row;

        InitializeComponent();
        HookNarrowMode();

        TitleBox.Text = row.Title;
        ArtistBox.Text = row.Artist;
        AlbumBox.Text = row.Album;

        // 只用于结果筛选下拉的显示名；搜索本身仍按全局设置的源顺序。
        if (sources is { Count: > 0 })
        {
            foreach (var source in sources)
                _sourceNames[source.Id] = Localizer.SourceName(source.Id, source.DisplayName);
        }

        UpdateFilterOptions([]);

        RuleCombo.ItemsSource = new[]
        {
            Localizer.T("Merge.FillEmptyOnly"),
            Localizer.T("Merge.Replace"),
            Localizer.T("Merge.KeepExisting"),
        };
        RuleCombo.SelectedIndex = 1;
    }

    public TagCandidate? Selected { get; private set; }

    public OverwriteRule Rule => RuleCombo.SelectedIndex switch
    {
        0 => OverwriteRule.FillEmptyOnly,
        2 => OverwriteRule.KeepExisting,
        _ => OverwriteRule.Replace,
    };

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (_search is null || _settings is null) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Progress.IsVisible = true;
        Status.Text = Localizer.T("Search.Searching");
        ResultsList.ItemsSource = null;
        _allResults = [];

        var query = new TagQuery
        {
            Title = TitleBox.Text,
            Artist = ArtistBox.Text,
            Album = AlbumBox.Text,
            FileName = _row?.FileName,
            Duration = _row?.Info?.Duration,
            // 携带文件句柄让 AcoustID 指纹源参与单曲识别。
            File = _row?.Source,
        };

        try
        {
            var results = await _search.SearchAsync(
                query,
                SourceCapability.Tags | SourceCapability.Cover | SourceCapability.Lyrics,
                _settings.Sources.Tags,
                _settings.Search.ItemsPerSource,
                _settings.Search.Threads,
                _cts.Token);

            _allResults = results;
            UpdateFilterOptions(results);
            ApplyFilter();

            // 解码失败等源级错误随结果一起提示（如缺 GStreamer 解码插件）。
            if (_search.Find("acoustid")?.LastError is { } decodeError)
                Status.Text = $"{Status.Text} · {decodeError}";
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

    /// <summary>按结果里实际出现的来源重建筛选项；保留当前选择（仍在结果中时）。</summary>
    private void UpdateFilterOptions(IReadOnlyList<TagCandidate> results)
    {
        _updatingFilter = true;
        try
        {
            var previous = (SourceFilterBox.SelectedItem as SourceFilterOption)?.Id;
            var options = new List<SourceFilterOption> { new(null, Localizer.T("Search.AllSources")) };
            foreach (var id in results.Select(r => r.SourceId)
                         .Where(id => !string.IsNullOrEmpty(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var label = _sourceNames.TryGetValue(id!, out var name) ? name : id;
                options.Add(new SourceFilterOption(id, label));
            }

            SourceFilterBox.ItemsSource = options;
            SourceFilterBox.SelectedItem = previous is { } previousId
                && options.FirstOrDefault(o =>
                    string.Equals(o.Id, previousId, StringComparison.OrdinalIgnoreCase)) is { } current
                    ? current
                    : options[0];
        }
        finally
        {
            _updatingFilter = false;
        }
    }

    private void OnSourceFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilter) ApplyFilter();
    }

    /// <summary>按当前筛选显示候选；状态栏跟随显示可见条数。</summary>
    private void ApplyFilter()
    {
        var filter = (SourceFilterBox.SelectedItem as SourceFilterOption)?.Id;
        IReadOnlyList<TagCandidate> visible = filter is null
            ? _allResults
            : _allResults
                .Where(r => string.Equals(r.SourceId, filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ResultsList.ItemsSource = visible;
        if (visible.Count > 0) ResultsList.SelectedItem = visible[0];

        Status.Text = visible.Count == 0
            ? Localizer.T("Search.NoResults")
            : Localizer.F("Search.CandidateCount", visible.Count);
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not TagCandidate candidate)
        {
            Status.Text = Localizer.T("Search.SelectCandidate");
            return;
        }

        Selected = candidate;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close(false);
    }
}
