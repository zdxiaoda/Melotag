namespace Melotag.UI.Views;

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Melotag.Core.Merge;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

public partial class MainWindow : Window
{
    private readonly UpdateUi _updates = new();

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        LocalizeColumns();
        Localizer.Instance.LanguageChanged += LocalizeColumns;
    }

    private void LocalizeColumns()
    {
        var columns = TracksGrid.Columns;
        if (columns.Count < 9) return;

        columns[0].Header = Localizer.T("Column.FileName");
        columns[1].Header = Localizer.T("Column.Title");
        columns[2].Header = Localizer.T("Column.Artist");
        columns[3].Header = Localizer.T("Column.Album");
        columns[4].Header = Localizer.T("Column.Year");
        columns[5].Header = Localizer.T("Column.Track");
        columns[6].Header = Localizer.T("Column.Duration");
        columns[7].Header = Localizer.T("Column.Format");
        columns[8].Header = Localizer.T("Column.Status");
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

    private static string BuildTsv(IEnumerable<TrackRowViewModel> rows)
    {
        static string Clean(string? value) =>
            (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        var builder = new StringBuilder();
        builder.AppendLine(string.Join('\t',
        [
            Localizer.T("Column.FileName"),
            Localizer.T("Column.Title"),
            Localizer.T("Column.Artist"),
            Localizer.T("Column.Album"),
            Localizer.T("Column.Year"),
            Localizer.T("Column.Track"),
            Localizer.T("Column.Duration"),
            Localizer.T("Column.Format"),
            Localizer.T("Column.Status"),
        ]));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join('\t',
            [
                Clean(row.FileName),
                Clean(row.Title),
                Clean(row.Artist),
                Clean(row.Album),
                Clean(row.YearText),
                Clean(row.TrackText),
                Clean(row.DurationText),
                Clean(row.FormatText),
                Clean(row.StatusText),
            ]));
        }

        return builder.ToString();
    }

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
