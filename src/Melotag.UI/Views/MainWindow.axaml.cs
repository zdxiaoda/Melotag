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
using Avalonia.VisualTree;
using Melotag.Core.Merge;
using Melotag.Core.Models;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class MainWindow : Window
{
    private readonly UpdateUi _updates = new();
    private bool _applyingLayout;
    private bool _forceClose;
    private bool _exitPromptOpen;

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
        TracksGrid.ContextRequested += OnTracksContextRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Vm is { } vm)
            vm.SortRefreshRequested += OnSortRefreshRequested;

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

        if (!column.IsVisible && Vm is { } vm && IsColumnSorted(column))
        {
            // 被隐藏的列如果正在参与排序，列表会变成“看不到排序键”的状态，直接清除排序。
            TracksGrid.CollectionView?.SortDescriptions.Clear();
            vm.SetSort(null, false);
            RefreshNativeView(force: true);
        }

        SaveColumnLayout();
        Vm?.SaveSettings();
    }

    private bool IsColumnSorted(DataGridColumn column)
    {
        if (Vm is null) return false;

        if (column.Tag is string id &&
            string.Equals(Vm.Settings.Ui.SortColumn, id, StringComparison.Ordinal))
            return true;

        var descriptions = TracksGrid.CollectionView?.SortDescriptions;
        if (descriptions is null || string.IsNullOrEmpty(column.SortMemberPath))
            return false;

        foreach (var description in descriptions)
        {
            if (string.Equals(description.PropertyPath, column.SortMemberPath, StringComparison.Ordinal))
                return true;
        }

        return false;
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

        // 配置里保存的排序键被隐藏（或重置列时被隐藏）时，不要留下不可见的排序。
        if (byId.Values.Any(column => !column.IsVisible && IsColumnSorted(column)))
        {
            TracksGrid.CollectionView?.SortDescriptions.Clear();
            Vm.SetSort(null, false);
            RefreshNativeView(force: true);
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
        if (Vm is null) return;
        if (e.Column.Tag is not string) return;

        // Sorting 在 DataGrid 真正修改 SortDescriptions 之前触发，推迟到下一轮读取实际状态，
        // 才能正确区分升/降序、Ctrl/Cmd 清除排序和 Shift 多列排序；程序化恢复排序也能安全复用。
        Dispatcher.UIThread.Post(SyncSortFromGrid);
    }

    private void SyncSortFromGrid()
    {
        if (Vm is null) return;

        var descriptions = TracksGrid.CollectionView?.SortDescriptions;
        if (descriptions is null) return;

        if (descriptions.Count == 0)
        {
            Vm.SetSort(null, false);
            // 清除排序后 DataGridCollectionView 已按旧源顺序刷新过一次，再按 VM 恢复的导入顺序重建。
            RefreshNativeView(force: true);
            return;
        }

        var description = descriptions[0];
        var column = TracksGrid.Columns.FirstOrDefault(c =>
            string.Equals(c.SortMemberPath, description.PropertyPath, StringComparison.Ordinal));

        if (column?.Tag is not string id) return;
        if (!TrackColumns.TryParseSortField(id, out var field)) return;

        Vm.SetSort(field, description.Direction == ListSortDirection.Descending);
    }

    private void OnSortRefreshRequested()
    {
        // 保存/重读/重命名/自动匹配等修改了排序键，原生排序视图不会自动重排。
        RefreshNativeView(force: Vm?.SortField is not null);
    }

    private void RefreshNativeView(bool force = false)
    {
        var view = TracksGrid.CollectionView;
        if (view is null) return;
        if (!force && view.SortDescriptions.Count == 0) return;

        var selectedItems = TracksGrid.SelectedItems;
        if (selectedItems is null) return;

        var previous = selectedItems.Cast<object>().ToList();
        var current = TracksGrid.SelectedItem as TrackRowViewModel ?? Vm?.SelectedTrack;

        // 恢复期间让 VM 忽略绑定的临时 null，避免选中项闪断触发封面反复解码。
        Vm?.BeginSelectionRestore();
        try
        {
            view.Refresh();

            // Refresh 可能替换 SelectedItems 实例，重新取一次。
            var items = TracksGrid.SelectedItems;
            if (items is null) return;

            // 只补回仍在列表里的行；不再无条件 Clear 整个选中集合。
            foreach (var item in previous)
            {
                if (item is TrackRowViewModel row && Vm?.Tracks.Contains(row) != true) continue;
                if (!items.Contains(item))
                    items.Add(item);
            }

            if (current is not null &&
                Vm?.Tracks.Contains(current) == true &&
                !ReferenceEquals(TracksGrid.SelectedItem, current))
                TracksGrid.SelectedItem = current;
        }
        finally
        {
            Vm?.EndSelectionRestore();
        }
    }

    private void RestoreSort()
    {
        if (Vm is null) return;
        if (!TrackColumns.TryParseSortField(Vm.Settings.Ui.SortColumn, out _)) return;

        var column = TracksGrid.Columns
            .FirstOrDefault(c => string.Equals(c.Tag as string, Vm.Settings.Ui.SortColumn, StringComparison.Ordinal));
        if (column is null) return;

        column.Sort(Vm.Settings.Ui.SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending);
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

    /// <summary>列表右键：单选与多选分别给出菜单；右键未选中的行时先切换为单行选中。</summary>
    private void OnTracksContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is not { } vm) return;

        var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is TrackRowViewModel track && !TracksGrid.SelectedItems.Contains(track))
        {
            TracksGrid.SelectedItems.Clear();
            TracksGrid.SelectedItems.Add(track);
            TracksGrid.SelectedItem = track;
            vm.SelectedTrack = track;
        }

        var selected = TracksGrid.SelectedItems?.OfType<TrackRowViewModel>().ToList() ?? [];
        if (selected.Count == 0) return;

        var items = selected.Count > 1 ? BuildMultiTrackMenu(selected) : BuildSingleTrackMenu();
        var flyout = new MenuFlyout
        {
            ItemsSource = items,
            Placement = PlacementMode.Pointer,
        };
        flyout.ShowAt(TracksGrid);
        e.Handled = true;
    }

    private IReadOnlyList<object> BuildSingleTrackMenu()
    {
        var vm = Vm;
        if (vm is null) return [];

        return
        [
            TrackMenu(Localizer.T("Menu.File.SaveCurrent"), () => vm.SaveSelectedCommand.Execute(null)),
            TrackMenu(Localizer.T("Toolbar.TagSearch"), () => OnTagSearch(null, new RoutedEventArgs())),
            TrackMenu(Localizer.T("Toolbar.Rename"), () => OnRename(null, new RoutedEventArgs())),
            TrackMenu(Localizer.T("Toolbar.Lyrics"), () => OnLyrics(null, new RoutedEventArgs())),
            TrackMenu(Localizer.T("Toolbar.Cover"), () => OnCover(null, new RoutedEventArgs())),
            TrackMenu(Localizer.T("Toolbar.History"), () => OnHistory(null, new RoutedEventArgs())),
            new Separator(),
            TrackMenu(Localizer.T("Menu.Edit.Remove"), () => vm.RemoveSelectedCommand.Execute(null)),
        ];
    }

    private IReadOnlyList<object> BuildMultiTrackMenu(IReadOnlyList<TrackRowViewModel> rows)
    {
        var vm = Vm;
        if (vm is null) return [];

        return
        [
            TrackMenu(Localizer.T("Context.SaveSelected"), () => _ = vm.SaveSelectedRowsAsync(rows)),
            TrackMenu(Localizer.T("Context.AutoMatchSelected"), () => _ = ShowAutoMatchAsync(rows)),
            TrackMenu(Localizer.T("Menu.Edit.ClearTags"), () => _ = vm.ClearTagsAsync(rows)),
            new Separator(),
            TrackMenu(Localizer.T("Context.RemoveSelected"), () => vm.RemoveRange(rows)),
        ];
    }

    private static MenuItem TrackMenu(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
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

        // 传快照：对话框存续期间列表可能被后台导入并发修改。
        var dialog = new RenameDialog(Vm.AllTracks.ToList(), new WindowsSafeNamePolicy(), Vm.HistoryStore);
        await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.Rename"), 780, 640);
        Vm.RefreshSort();
    }

    private async void OnAutoMatch(object? sender, RoutedEventArgs e) => await ShowAutoMatchAsync(null);

    /// <summary>rows 为 null 时处理全部；多选右键时只处理选中行。</summary>
    private async Task ShowAutoMatchAsync(IReadOnlyList<TrackRowViewModel>? rows)
    {
        if (Vm is null) return;

        var count = rows?.Count ?? Vm.AllTracks.Count;
        var dialog = new AutoMatchDialog(Vm.Settings, Vm.Search.Sources, count);
        if (await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.AutoMatch"), 860, 720) is not true ||
            dialog.Options is not { } options)
            return;

        if (rows is null)
            await Vm.AutoMatchAllAsync(options);
        else
            await Vm.AutoMatchAsync(rows, options);
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

        var dialog = new TagSearchDialog(Vm.Search, Vm.Settings, row, Vm.Search.Sources);
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

    /// <summary>有未保存修改时先确认；保存失败则留在当前窗口。</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_forceClose || _exitPromptOpen || Vm is not { HasUnsavedChanges: true } vm)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _ = ConfirmExitAsync(vm);
    }

    private async Task ConfirmExitAsync(MainWindowViewModel vm)
    {
        if (_exitPromptOpen) return;
        _exitPromptOpen = true;
        try
        {
            var dialog = new ExitConfirmDialog(vm.UnsavedCount);
            var result = await ModalHost.ShowAsync(this, dialog, Localizer.T("Dialog.ExitConfirm"), 480, 280);

            if (result is not ExitChoice choice || choice == ExitChoice.Cancel) return;

            if (choice == ExitChoice.SaveAndExit)
            {
                await vm.SaveUnsavedAsync();
                // 保存失败/被其他操作占用时不要静默丢掉修改。
                if (vm.HasUnsavedChanges) return;
            }

            _forceClose = true;
            Close();
        }
        finally
        {
            _exitPromptOpen = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Localizer.Instance.LanguageChanged -= LocalizeColumns;
        if (Vm is { } vm)
        {
            vm.SortRefreshRequested -= OnSortRefreshRequested;
            vm.SaveSettings();
            // 退订全局语言事件并取消仍在跑的导入/匹配，避免 VM 比窗口活得更久。
            vm.Dispose();
        }

        base.OnClosed(e);
    }
}
