namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.Core.Rename;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class RenameDialog : ModalView
{
    private sealed record PreviewRow(string Source, string Target, string Note);
    private sealed record FieldOption(string Name, FieldKey? Field);

    private static FieldOption[] CreateFieldOptions() =>
    [
        new(Localizer.T("Field.None"), null),
        new(Localizer.T("Field.Title"), FieldKey.Title),
        new(Localizer.T("Field.Artist"), FieldKey.Artist),
        new(Localizer.T("Field.Album"), FieldKey.Album),
        new(Localizer.T("Field.AlbumArtist"), FieldKey.AlbumArtist),
        new(Localizer.T("Field.Year"), FieldKey.Year),
        new(Localizer.T("Field.Track"), FieldKey.Track),
        new(Localizer.T("Field.Disc"), FieldKey.Disc),
        new(Localizer.T("Field.Genre"), FieldKey.Genre),
        new(Localizer.T("Field.Comment"), FieldKey.Comment),
    ];

    private readonly IReadOnlyList<TrackRowViewModel> _rows = [];
    private readonly ISafeNamePolicy _policy = new WindowsSafeNamePolicy();
    private readonly IHistoryStore? _history;
    private readonly RenameService _rename = new();
    private readonly List<(int Group, ComboBox Box)> _groupBoxes = [];

    public RenameDialog()
    {
        InitializeComponent();
    }

    public RenameDialog(IReadOnlyList<TrackRowViewModel> rows, ISafeNamePolicy policy, IHistoryStore? history)
    {
        _rows = rows;
        _policy = policy;
        _history = history;

        InitializeComponent();

        PresetCombo.ItemsSource = RenamePresets.Defaults;
        BuildGroupRows();
        PatternBox.Text = RenamePresets.DefaultPattern;
        PresetCombo.SelectedItem = RenamePresets.DefaultPattern;
        RefreshPreview();
    }

    private void BuildGroupRows()
    {
        var fieldOptions = CreateFieldOptions();
        for (var group = 1; group <= 6; group++)
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 14, 4) };
            panel.Children.Add(new TextBlock { Text = Localizer.F("Rename.Group", group), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var combo = new ComboBox { Width = 132, ItemsSource = fieldOptions, SelectedIndex = 0 };
            panel.Children.Add(combo);
            GroupRows.Children.Add(panel);
            _groupBoxes.Add((group, combo));
        }
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (PanelRename is null || PanelParse is null) return;
        var renameMode = ModeRename.IsChecked == true;
        PanelRename.IsVisible = renameMode;
        PanelParse.IsVisible = !renameMode;
        RefreshPreview();
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is string pattern)
        {
            PatternBox.Text = pattern;
            RefreshPreview();
        }
    }

    private void OnPatternChanged(object? sender, TextChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (PreviewList is null || Message is null) return;

        var rows = new List<PreviewRow>();

        if (ModeRename.IsChecked != false)
        {
            var pattern = PatternBox.Text ?? "";
            var errors = NamePattern.Validate(pattern, PatternDirection.Generate);
            if (errors.Count > 0)
            {
                Message.Text = Localizer.T($"PatternError.{errors[0]}");
                PreviewList.ItemsSource = rows;
                return;
            }

            var ready = 0;
            foreach (var row in _rows)
            {
                var preview = _rename.Preview(row.Path, row.ToTags(), pattern, _policy);
                if (preview.IsValid) ready++;
                var note = preview.Error == RenameService.EmptyNameError
                    ? Localizer.T("Rename.EmptyName")
                    : preview.Error ?? (preview.IsValid ? Localizer.T("Rename.Ready") : Localizer.T("Rename.NoChange"));
                rows.Add(new PreviewRow(preview.SourceName, preview.TargetName, note));
            }

            Message.Text = Localizer.F("Rename.FileCount", rows.Count, ready);
        }
        else
        {
            var plan = TryBuildPlan(out var error);
            if (plan is null)
            {
                Message.Text = error ?? Localizer.T("Rename.InvalidRegex");
                PreviewList.ItemsSource = rows;
                return;
            }

            var matched = 0;
            foreach (var row in _rows)
            {
                var fileName = Path.GetFileNameWithoutExtension(row.Path);
                var tags = plan.Map(fileName);
                if (tags is null)
                {
                    rows.Add(new PreviewRow(row.FileName, "—", Localizer.T("Rename.NoMatch")));
                    continue;
                }

                matched++;
                rows.Add(new PreviewRow(row.FileName, DescribeTags(tags), Localizer.T("Rename.Parsed")));
            }

            Message.Text = Localizer.F("Rename.MatchCount", rows.Count, matched);
        }

        PreviewList.ItemsSource = rows;
    }

    private static string DescribeTags(TrackTags tags) => string.Join(" | ",
        new[] { tags.Title, tags.Artist, tags.Album, tags.Year?.ToString(), tags.Track?.ToString() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private RegexRenamePlan? TryBuildPlan(out string? error)
    {
        error = null;
        var pattern = RegexBox.Text ?? "";
        var map = new Dictionary<int, FieldKey>();
        foreach (var (group, box) in _groupBoxes)
        {
            if (box.SelectedItem is not FieldOption { Field: { } field }) continue;
            if (map.ContainsValue(field))
            {
                error = Localizer.T("Rename.DuplicateField");
                return null;
            }
            map[group] = field;
        }

        if (map.Count == 0)
        {
            error = Localizer.T("Rename.AtLeastOneGroup");
            return null;
        }

        try
        {
            return RegexRenamePlan.Create(pattern, map);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (ModeRename.IsChecked != false)
            ApplyRename();
        else
            ApplyParse();
    }

    private void ApplyRename()
    {
        var pattern = PatternBox.Text ?? "";
        var applied = 0;
        var failed = 0;

        foreach (var row in _rows)
        {
            var preview = _rename.Preview(row.Path, row.ToTags(), pattern, _policy);
            if (!preview.IsValid)
            {
                failed++;
                continue;
            }

            try
            {
                var tags = row.ToTags();
                var result = _rename.Apply(preview, AlsoLrc.IsChecked == true, AlsoPic.IsChecked == true);
                row.RenameTo(result.NewPath);
                _history?.AddSnapshot(result.NewPath, tags);
                applied++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        Message.Text = Localizer.F("Rename.Applied", applied, failed);
        if (applied > 0) Close(true);
    }

    private void ApplyParse()
    {
        var plan = TryBuildPlan(out var error);
        if (plan is null)
        {
            Message.Text = error ?? Localizer.T("Rename.InvalidRegex");
            return;
        }

        var applied = 0;
        foreach (var row in _rows)
        {
            var tags = plan.Map(Path.GetFileNameWithoutExtension(row.Path));
            if (tags is null) continue;
            row.ApplyTags(tags);
            applied++;
        }

        Message.Text = Localizer.F("Rename.TagsUpdated", applied);
        if (applied > 0) Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
