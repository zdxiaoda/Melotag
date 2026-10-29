namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.Core.Rename;
using Melotag.UI.ViewModels;

public partial class RenameDialog : Window
{
    private sealed record PreviewRow(string Source, string Target, string Note);
    private sealed record FieldOption(string Name, FieldKey? Field);

    private static readonly FieldOption[] FieldOptions =
    [
        new("不使用", null),
        new("标题", FieldKey.Title),
        new("艺术家", FieldKey.Artist),
        new("专辑", FieldKey.Album),
        new("专辑艺术家", FieldKey.AlbumArtist),
        new("年份", FieldKey.Year),
        new("音轨", FieldKey.Track),
        new("碟号", FieldKey.Disc),
        new("流派", FieldKey.Genre),
        new("注释", FieldKey.Comment),
    ];

    private readonly IReadOnlyList<TrackRowViewModel> _rows;
    private readonly ISafeNamePolicy _policy;
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
        PatternBox.Text = RenamePresets.Defaults[0];
        PresetCombo.SelectedIndex = 0;
        RefreshPreview();
    }

    private void BuildGroupRows()
    {
        for (var group = 1; group <= 6; group++)
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 14, 4) };
            panel.Children.Add(new TextBlock { Text = $"组 {group}", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var combo = new ComboBox { Width = 132, ItemsSource = FieldOptions, SelectedIndex = 0 };
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
        if (PreviewGrid is null || Message is null) return;

        var rows = new List<PreviewRow>();

        if (ModeRename.IsChecked != false)
        {
            var pattern = PatternBox.Text ?? "";
            var errors = NamePattern.Validate(pattern, PatternDirection.Generate);
            if (errors.Count > 0)
            {
                Message.Text = RenameService.Describe(errors[0]);
                PreviewGrid.ItemsSource = rows;
                return;
            }

            foreach (var row in _rows)
            {
                var preview = _rename.Preview(row.Path, row.ToTags(), pattern, _policy);
                rows.Add(new PreviewRow(
                    preview.SourceName,
                    preview.TargetName,
                    preview.Error ?? (preview.IsValid ? "就绪" : "无变化")));
            }

            var ready = rows.Count(r => r.Note == "就绪");
            Message.Text = $"{rows.Count} 个文件，{ready} 个可重命名";
        }
        else
        {
            var plan = TryBuildPlan(out var error);
            if (plan is null)
            {
                Message.Text = error ?? "正则无效";
                PreviewGrid.ItemsSource = rows;
                return;
            }

            foreach (var row in _rows)
            {
                var fileName = Path.GetFileNameWithoutExtension(row.Path);
                var tags = plan.Map(fileName);
                if (tags is null)
                {
                    rows.Add(new PreviewRow(row.FileName, "—", "文件名不匹配"));
                    continue;
                }

                rows.Add(new PreviewRow(row.FileName, DescribeTags(tags), "解析成功"));
            }

            var matched = rows.Count(r => r.Note == "解析成功");
            Message.Text = $"{rows.Count} 个文件，{matched} 个匹配";
        }

        PreviewGrid.ItemsSource = rows;
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
                error = "同一字段不能映射多个捕获组";
                return null;
            }
            map[group] = field;
        }

        if (map.Count == 0)
        {
            error = "至少映射一个捕获组";
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

        Message.Text = $"已完成 {applied} 个，失败/跳过 {failed} 个";
        if (applied > 0) Close(true);
    }

    private void ApplyParse()
    {
        var plan = TryBuildPlan(out var error);
        if (plan is null)
        {
            Message.Text = error ?? "正则无效";
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

        Message.Text = $"已更新 {applied} 个文件的标签";
        if (applied > 0) Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
