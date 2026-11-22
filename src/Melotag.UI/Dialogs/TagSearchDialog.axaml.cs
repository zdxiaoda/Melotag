namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Merge;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

/// <summary>结果列表的展示行：空字段自动省略，来源显示为本地化名称。</summary>
public sealed record TagSearchResultRow(
    TagCandidate Candidate,
    string Title,
    string Meta,
    string SourceLabel,
    string Detail);

public partial class TagSearchDialog : ModalView
{
    private readonly TagSearchService? _search;
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private readonly Dictionary<string, string> _sourceNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TagCandidate> _allResults = [];
    private bool _updatingFilter;
    private CancellationTokenSource? _cts;
    private int _searchVersion;

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

        // 每次搜索拿一个新的版本号；迟到的旧结果直接丢弃，进度条也只有最新一次能收起。
        var version = ++_searchVersion;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Progress.IsVisible = true;
        Status.Text = Localizer.T("Search.Searching");
        ResultsList.ItemsSource = null;
        _allResults = [];
        EmptyHint.IsVisible = true;

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
                token);

            if (version != _searchVersion) return;

            _allResults = results;
            UpdateFilterOptions(results);
            ApplyFilter();

            // 解码失败等源级错误随结果一起提示（如缺 GStreamer 解码插件）。
            var acoustId = _search.Find("acoustid");
            if (acoustId?.LastError is { } acoustIdError)
                Status.Text = $"{Status.Text} · {acoustIdError}";
            else if (results.Count == 0
                     && acoustId is { IsConfigured: true }
                     && _row?.Source is not null
                     && _settings.Sources.Tags.Contains("acoustid", StringComparer.OrdinalIgnoreCase))
                Status.Text = $"{Status.Text} · {Localizer.T("Search.AcoustIdNoMatch")}";
        }
        catch (OperationCanceledException)
        {
            if (version == _searchVersion)
                Status.Text = Localizer.T("Search.Cancelled");
        }
        catch (Exception ex)
        {
            if (version == _searchVersion)
                Status.Text = Localizer.F("Search.Failed", ex.Message);
        }
        finally
        {
            if (version == _searchVersion)
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

        var rows = visible.Select(ToRow).ToList();
        ResultsList.ItemsSource = rows;
        EmptyHint.IsVisible = rows.Count == 0;
        if (rows.Count > 0) ResultsList.SelectedItem = rows[0];

        Status.Text = rows.Count == 0
            ? Localizer.T("Search.NoResults")
            : Localizer.F("Search.CandidateCount", rows.Count);
    }

    private TagSearchResultRow ToRow(TagCandidate candidate)
    {
        var meta = string.Join(" · ", new[]
        {
            candidate.Artist,
            candidate.Album,
            candidate.Year?.ToString(),
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var source = _sourceNames.TryGetValue(candidate.SourceId, out var name)
            ? name
            : candidate.SourceId;

        return new TagSearchResultRow(candidate, candidate.Title, meta, source, candidate.Detail ?? "");
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not TagSearchResultRow row)
        {
            Status.Text = Localizer.T("Search.SelectCandidate");
            return;
        }

        Selected = row.Candidate;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        // 关闭后不再接受任何迟到的搜索结果。
        _searchVersion++;
        _cts?.Cancel();
        Close(false);
    }
}
