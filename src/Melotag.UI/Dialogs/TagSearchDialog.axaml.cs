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

        var query = new TagQuery
        {
            Title = TitleBox.Text,
            Artist = ArtistBox.Text,
            Album = AlbumBox.Text,
            FileName = _row?.FileName,
            Duration = _row?.Info?.Duration,
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

            ResultsList.ItemsSource = results;
            if (results.Count > 0) ResultsList.SelectedItem = results[0];
            Status.Text = results.Count == 0
                ? Localizer.T("Search.NoResults")
                : Localizer.F("Search.CandidateCount", results.Count);
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
