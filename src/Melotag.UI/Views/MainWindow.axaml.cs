namespace Melotag.UI.Views;

using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Melotag.Core.Merge;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class MainWindow : Window
{
    private readonly UpdateUi _updates = new();
    private bool _applyingLayout;

    /// <summary>程序化恢复排序时挂起，用来吞掉随之而来的 Sorting 事件（该事件是异步派发的）。</summary>
    private DataGridColumn? _pendingRestoreColumn;
    private DispatcherTimer? _pendingRestoreTimer;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        LocalizeColumns();
        Localizer.Instance.LanguageChanged += LocalizeColumns;

        foreach (var column in TracksGrid.Columns)
            column.HeaderPointerPressed += (_, e) => OnColumnHeaderPointerPressed(e);

        TracksGrid.Sorting += OnGridSorting;
        TracksGrid.ColumnReordered += (_, _) => OnColumnReordered();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyColumnLayout();
        RestoreSort();
    }

    private void LocalizeColumns()
    {
        foreach (var column in TracksGrid.Columns)
        {
            if (column.Tag is string id)
                column.Header = Localizer.T(TrackColumns.LabelKey(id));
        }
    }

    /// <summary>按列目录顺序给表头右键菜单提供勾选项；右键任意表头即可打开。</summary>
    private void OnColumnHeaderPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TracksGrid).Properties.IsRightButtonPressed) return;

        e.Handled = true;

        var items = new List<object>();
        foreach (var entry in TracksGrid.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (entry.Tag is not string id) continue;

            var item = new MenuItem
            {
                Header = Localizer.T(TrackColumns.LabelKey(id)),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = entry.IsVisible,
                StaysOpenOnClick = true,
                // 文件名是列表的锚点，不允许隐藏。
                IsEnabled = id != TrackColumns.FileName,
            };
            item.Click += (_, _) => ToggleColumn(entry, item);
            items.Add(item);
        }

        items.Add(new Separator());

        var reset = new MenuItem { Header = Localizer.T("Column.ResetLayout") };
        reset.Click += (_, _) => ResetColumnLayout();
        items.Add(reset);

        var flyout = new MenuFlyout
        {
            ItemsSource = items,
            Placement = PlacementMode.Pointer,
        };
        flyout.ShowAt(TracksGrid);
    }

    private void ToggleColumn(DataGridColumn column, MenuItem item)
    {
        var id = column.Tag as string;
        if (id == TrackColumns.FileName && item.IsChecked != true)
        {
            item.IsChecked = true;
            return;
        }

        column.IsVisible = item.IsChecked == true;
        SaveColumnLayout();
        Vm?.SaveSettings();
    }

    private void ApplyColumnLayout()
    {
        if (Vm is null) return;

        var saved = Vm.Settings.Ui.VisibleColumns;
        var visible = saved.Count > 0
            ? saved.Where(TrackColumns.IsKnown).Distinct(StringComparer.Ordinal).ToList()
            : TrackColumns.DefaultVisible.ToList();

        if (!visible.Contains(TrackColumns.FileName))
            visible.Insert(0, TrackColumns.FileName);

        var byId = TracksGrid.Columns
            .Where(c => c.Tag is string)
            .ToDictionary(c => (string)c.Tag!, c => c);

        var visibleSet = visible.ToHashSet(StringComparer.Ordinal);

        _applyingLayout = true;
        try
        {
            // 先统一切换可见性，再排 DisplayIndex；穿插执行会被 IsVisible 的重新索引覆盖。
            foreach (var (id, column) in byId)
                column.IsVisible = visibleSet.Contains(id);

            var index = 0;
            foreach (var id in visible)
            {
                if (byId.TryGetValue(id, out var column))
                    column.DisplayIndex = index++;
            }

            foreach (var id in TrackColumns.All)
            {
                if (visibleSet.Contains(id)) continue;
                if (byId.TryGetValue(id, out var column))
                    column.DisplayIndex = index++;
            }
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void ResetColumnLayout()
    {
        if (Vm is null) return;

        Vm.Settings.Ui.VisibleColumns = [];
        ApplyColumnLayout();
        Vm.SaveSettings();
    }

    private void SaveColumnLayout()
    {
        if (Vm is null || TracksGrid.Columns.Count == 0) return;

        var visible = TracksGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Where(c => c.IsVisible && c.Tag is string)
            .Select(c => (string)c.Tag!)
            .ToList();

        // 布局尚未就绪时不要把配置写成空列表。
        if (visible.Count > 0)
            Vm.Settings.Ui.VisibleColumns = visible;
    }

    private void OnColumnReordered()
    {
        if (_applyingLayout || Vm is null) return;

        SaveColumnLayout();
        Vm.SaveSettings();
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (ReferenceEquals(_pendingRestoreColumn, e.Column))
        {
            ClearPendingRestore();
            return;
        }

        if (Vm is null) return;
        if (e.Column.Tag is not string id) return;

        var same = string.Equals(Vm.Settings.Ui.SortColumn, id, StringComparison.Ordinal);
        Vm.Settings.Ui.SortColumn = id;
        Vm.Settings.Ui.SortDescending = same && !Vm.Settings.Ui.SortDescending;
        Vm.SaveSettings();
    }

    private void RestoreSort()
    {
        if (Vm is null) return;
        if (!TrackColumns.IsKnown(Vm.Settings.Ui.SortColumn)) return;

        var column = TracksGrid.Columns
            .FirstOrDefault(c => string.Equals(c.Tag as string, Vm.Settings.Ui.SortColumn, StringComparison.Ordinal));
        if (column is null) return;

        _pendingRestoreColumn = column;
        _pendingRestoreTimer?.Stop();

        // Sorting 事件是异步派发的；如果平台没有派发，最多挂起 1.5 秒。
        _pendingRestoreTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal,
            (_, _) => ClearPendingRestore());
        _pendingRestoreTimer.Start();

        column.Sort(Vm.Settings.Ui.SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending);
    }

    private void ClearPendingRestore()
    {
        _pendingRestoreColumn = null;
        _pendingRestoreTimer?.Stop();
        _pendingRestoreTimer = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is null) return;

        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;

        var paths = items
            .Select(i => i.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count > 0) await Vm.AddPathsAsync(paths);
    }

    private async void OnTracksKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null) return;

        var selected = TracksGrid.SelectedItems?.OfType<TrackRowViewModel>().ToList() ?? [];
        if (selected.Count == 0) return;

        if (e.Key == Key.Delete)
        {
            Vm.RemoveRange(selected);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            try
            {
                if (clipboard is not null) await clipboard.SetTextAsync(BuildTsv(selected));
                Vm.StatusText = Localizer.F("Status.Copied", selected.Count);
            }
            catch
            {
                Vm.StatusText = Localizer.T("Status.CopyFailed");
            }

            e.Handled = true;
        }
    }

    /// <summary>复制为 TSV，列集合跟随当前可见列。</summary>
    private string BuildTsv(IEnumerable<TrackRowViewModel> rows)
    {
        static string Clean(string? value) =>
            (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        var ids = TracksGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Where(c => c.IsVisible && c.Tag is string)
            .Select(c => (string)c.Tag!)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine(string.Join('\t', ids.Select(id => Localizer.T(TrackColumns.LabelKey(id)))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join('\t', ids.Select(id => Clean(CellValue(row, id)))));
        }

        return builder.ToString();
    }

    private static string CellValue(TrackRowViewModel row, string id) => id switch
    {
        TrackColumns.FileName => row.FileName,
        TrackColumns.Title => row.Title ?? "",
        TrackColumns.Artist => row.Artist ?? "",
        TrackColumns.Album => row.Album ?? "",
        TrackColumns.AlbumArtist => row.AlbumArtist ?? "",
        TrackColumns.Year => row.YearText ?? "",
        TrackColumns.Track => row.TrackText ?? "",
        TrackColumns.Disc => row.DiscText ?? "",
        TrackColumns.Genre => row.Genre ?? "",
        TrackColumns.Duration => row.DurationText,
        TrackColumns.Size => row.SizeText,
        TrackColumns.Format => row.FormatText,
        TrackColumns.Bitrate => row.BitrateText,
        TrackColumns.SampleRate => row.SampleRateText,
        TrackColumns.Channels => row.ChannelsText,
        TrackColumns.Modified => row.ModifiedText,
        TrackColumns.Created => row.CreatedText,
        TrackColumns.Status => row.StatusText,
        _ => "",
    };

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var files = await Platform.PlatformServices.FilePicker.PickFilesAsync(this);
        if (files.Count > 0) await Vm.AddFilesAsync(files);
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var files = await Platform.PlatformServices.FilePicker.PickFolderAsync(this, Vm.CanHandle);
        if (files.Count > 0) await Vm.AddFilesAsync(files);
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (Vm is not null) Vm.FilterText = FilterBox.Text ?? "";
    }

    private void OnFocusFilter(object? sender, RoutedEventArgs e) => FilterBox.Focus();

    private async void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.SaveAllAsync();
    }

    private async void OnClearTags(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ClearTagsAsync();
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.AllTracks.Count == 0)
        {
            Vm.StatusText = Localizer.T("Status.EmptyList");
            return;
        }

        var dialog = new RenameDialog(Vm.AllTracks, new WindowsSafeNamePolicy(), Vm.HistoryStore);
        await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.Rename"), 780, 640);
    }

    private async void OnAutoMatch(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new AutoMatchDialog(Vm.Settings, Vm.Search.Sources, Vm.AllTracks.Count);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.AutoMatch"), 860, 720) is true &&
            dialog.Options is { } options)
            await Vm.AutoMatchAllAsync(options);
    }

    private async void OnTagsToTraditional(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertTagsAsync(toTraditional: true);
    }

    private async void OnTagsToSimplified(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertTagsAsync(toTraditional: false);
    }

    private async void OnNamesToTraditional(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertFileNamesAsync(toTraditional: true, new WindowsSafeNamePolicy());
    }

    private async void OnNamesToSimplified(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertFileNamesAsync(toTraditional: false, new WindowsSafeNamePolicy());
    }

    private async void OnExtractCovers(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExtractCoversAsync();
    }

    private async void OnTagSearch(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        var dialog = new TagSearchDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.TagSearch"), 940, 640) is true && dialog.Selected is { } candidate)
            await Vm.ApplyCandidateAsync(row, candidate, dialog.Rule);
    }

    private async void OnLyrics(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        var dialog = new LyricsDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.Lyrics"), 860, 680) is true)
        {
            row.Lyrics = dialog.ResultLyrics;
            Vm.StatusText = Localizer.T("Status.LyricsUpdated");
        }
    }

    private async void OnCover(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        var dialog = new CoverDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.Cover"), 820, 600) is not true) return;

        if (dialog.Removed)
        {
            row.ClearCover();
            Vm.StatusText = Localizer.T("Status.CoverRemoved");
        }
        else if (dialog.Result is { } data)
        {
            row.SetCover(data, dialog.ResultMime);
            Vm.StatusText = Localizer.T("Status.CoverUpdated");
        }
    }

    private async void OnHistory(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        var dialog = new HistoryDialog(Vm.HistoryStore, row.Key,
            entry => _ = Vm.RestoreHistoryAsync(row, entry));
        await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.History"), 820, 560);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new SettingsDialog(Vm.Settings, Vm.Search.Sources);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.Settings"), 760, 840) is true)
            Vm.SaveSettings();
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app) return;

        var next = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = next;

        if (Vm is { } vm)
        {
            vm.Settings.Ui.Theme = next == ThemeVariant.Dark ? "Dark" : "Light";
            vm.SaveSettings();
        }
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = Localizer.T("App.AboutTitle"),
            Icon = Icon,
            Width = 500,
            Height = 430,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(20),
                Child = AboutContent.Create(this),
            },
        };

        await dialog.ShowDialog(this);
    }

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) => await _updates.ShowManualAsync(this);

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Localizer.Instance.LanguageChanged -= LocalizeColumns;
        Vm?.SaveSettings();
        base.OnClosed(e);
    }
}
