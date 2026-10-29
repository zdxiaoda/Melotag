namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Merge;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.ViewModels;

public partial class TagSearchDialog : Window
{
    private readonly TagSearchService? _search;
    private readonly AppSettings? _settings;
    private readonly TrackRowViewModel? _row;
    private CancellationTokenSource? _cts;

    public TagSearchDialog()
    {
        InitializeComponent();
    }

    public TagSearchDialog(TagSearchService search, AppSettings settings, TrackRowViewModel row)
    {
        _search = search;
        _settings = settings;
        _row = row;

        InitializeComponent();

        TitleBox.Text = row.Title;
        ArtistBox.Text = row.Artist;
        AlbumBox.Text = row.Album;

        RuleCombo.ItemsSource = new[] { "只填空字段", "全部覆盖", "保留现有" };
        RuleCombo.SelectedIndex = 0;
    }

    public TagCandidate? Selected { get; private set; }

    public OverwriteRule Rule => RuleCombo.SelectedIndex switch
    {
        1 => OverwriteRule.Replace,
        2 => OverwriteRule.KeepExisting,
        _ => OverwriteRule.FillEmptyOnly,
    };

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (_search is null || _settings is null) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Progress.IsVisible = true;
        Status.Text = "正在搜索…";
        ResultsGrid.ItemsSource = null;

        var query = new TagQuery
        {
            Title = TitleBox.Text,
            Artist = ArtistBox.Text,
            Album = AlbumBox.Text,
            FileName = _row?.FileName,
        };

        try
        {
            var results = await _search.SearchAsync(
                query,
                SourceCapability.Tags | SourceCapability.Cover,
                _settings.Sources.Tags,
                _settings.Search.ItemsPerSource,
                _settings.Search.Threads,
                _cts.Token);

            ResultsGrid.ItemsSource = results;
            if (results.Count > 0) ResultsGrid.SelectedItem = results[0];
            Status.Text = results.Count == 0 ? "没有结果（检查网络或 Last.fm API key）" : $"共 {results.Count} 条候选";
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

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not TagCandidate candidate)
        {
            Status.Text = "请先选择一条候选";
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
