namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Merge;
using Melotag.Core.Models;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

/// <summary>批量自动匹配前的选项：勾选字段、在线源与覆盖方式。</summary>
public partial class AutoMatchDialog : ModalView
{
    public AutoMatchDialog()
    {
        InitializeComponent();
    }

    public AutoMatchDialog(AppSettings settings, IReadOnlyList<ITagSource> sources, int fileCount)
    {
        InitializeComponent();

        FileCountText.Text = Localizer.F("AutoMatch.FileCount", fileCount);
        TagSources.Load(sources, settings.Sources.Tags, SourceDefaults.Tags);

        RuleCombo.ItemsSource = new[]
        {
            Localizer.T("Merge.FillEmptyOnly"),
            Localizer.T("Merge.Replace"),
            Localizer.T("Merge.KeepExisting"),
        };
        RuleCombo.SelectedIndex = 1;
    }

    public AutoMatchOptions? Options { get; private set; }

    private OverwriteRule Rule => RuleCombo.SelectedIndex switch
    {
        1 => OverwriteRule.Replace,
        2 => OverwriteRule.KeepExisting,
        _ => OverwriteRule.FillEmptyOnly,
    };

    private HashSet<FieldKey> SelectedFields()
    {
        var fields = new HashSet<FieldKey>();
        if (TitleCheck.IsChecked == true) fields.Add(FieldKey.Title);
        if (ArtistCheck.IsChecked == true) fields.Add(FieldKey.Artist);
        if (AlbumCheck.IsChecked == true) fields.Add(FieldKey.Album);
        if (AlbumArtistCheck.IsChecked == true) fields.Add(FieldKey.AlbumArtist);
        if (YearCheck.IsChecked == true) fields.Add(FieldKey.Year);
        if (TrackCheck.IsChecked == true) fields.Add(FieldKey.Track);
        if (DiscCheck.IsChecked == true) fields.Add(FieldKey.Disc);
        if (GenreCheck.IsChecked == true) fields.Add(FieldKey.Genre);
        if (CommentCheck.IsChecked == true) fields.Add(FieldKey.Comment);
        if (ComposerCheck.IsChecked == true) fields.Add(FieldKey.Composer);
        if (LyricsCheck.IsChecked == true) fields.Add(FieldKey.Lyrics);
        return fields;
    }

    private void OnStart(object? sender, RoutedEventArgs e)
    {
        var fields = SelectedFields();
        var includeCover = CoverCheck.IsChecked == true;

        if (fields.Count == 0 && !includeCover)
        {
            Message.Text = Localizer.T("AutoMatch.NeedField");
            return;
        }

        var sourceOrder = TagSources.GetOrderedIds();
        if (sourceOrder.Count == 0)
        {
            Message.Text = Localizer.T("AutoMatch.NeedSource");
            return;
        }

        Options = new AutoMatchOptions(Rule, fields, includeCover, sourceOrder);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
