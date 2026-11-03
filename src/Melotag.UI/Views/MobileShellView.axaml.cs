namespace Melotag.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.Platform;
using Melotag.UI.ViewModels;

/// <summary>
/// Android（单视图）主界面：一个列表页 + 一个详情页，对话框全部走 ModalHost 浮层。
/// </summary>
public partial class MobileShellView : UserControl
{
    private readonly UpdateUi _updates = new();

    public MobileShellView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
                vm.Tracks.CollectionChanged += (_, _) => UpdateEmptyHint();
            UpdateEmptyHint();
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private TopLevel? Top => TopLevel.GetTopLevel(this);

    private void UpdateEmptyHint()
    {
        if (Vm is null) return;
        EmptyHint.IsVisible = Vm.Tracks.Count == 0;
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (Vm is not null) Vm.FilterText = FilterBox.Text ?? "";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 选中项被移除（或过滤掉）时退回列表；打开详情只由用户点击触发。
        if (TrackList.SelectedItem is null && DetailPage.IsVisible)
            ShowDetail(false);
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (TrackList.SelectedItem is TrackRowViewModel)
            ShowDetail(true);
    }

    private void OnBack(object? sender, RoutedEventArgs e) => ShowDetail(false);

    private void ShowDetail(bool detail)
    {
        DetailPage.IsVisible = detail;
        ListPage.IsVisible = !detail;
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Top is not { } top) return;

        var files = await PlatformServices.FilePicker.PickFilesAsync(top);
        if (files.Count > 0) await Vm.AddFilesAsync(files);
        UpdateEmptyHint();
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Top is not { } top) return;

        if (!PlatformServices.FilePicker.SupportsFolderPick)
        {
            Vm.StatusText = Localizer.T("Status.FolderPickUnsupported");
            return;
        }

        Vm.StatusText = Localizer.T("Status.FolderScanning");
        var files = await PlatformServices.FilePicker.PickFolderAsync(top, Vm.CanHandle);
        if (files.Count > 0) await Vm.AddFilesAsync(files);
        else Vm.StatusText = Localizer.T("Status.Ready");
        UpdateEmptyHint();
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ReloadCommand.ExecuteAsync(null);
    }

    private async void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.SaveAllAsync();
    }

    private async void OnSaveSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.SaveSelectedCommand.ExecuteAsync(null);
    }

    private async void OnClearTags(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ClearTagsAsync();
    }

    private async void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        await Vm.RemoveSelectedCommand.ExecuteAsync(null);
        ShowDetail(false);
        UpdateEmptyHint();
    }

    private async void OnAutoMatch(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new AutoMatchDialog(Vm.Settings, Vm.Search.Sources, Vm.AllTracks.Count);
        if (await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.AutoMatch"), 860, 720) is true &&
            dialog.Options is { } options)
            await Vm.AutoMatchAllAsync(options);
    }

    private async void OnExtractCovers(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExtractCoversAsync();
    }

    private async void OnTagsToTraditional(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertTagsAsync(toTraditional: true);
    }

    private async void OnTagsToSimplified(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ConvertTagsAsync(toTraditional: false);
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        if (!Vm.AllTracks.Any(r => r.Source?.CanRename == true))
        {
            Vm.StatusText = Localizer.T("Status.AndroidRenameUnsupported");
            return;
        }

        if (Vm.AllTracks.Count == 0)
        {
            Vm.StatusText = Localizer.T("Status.EmptyList");
            return;
        }

        var dialog = new RenameDialog(Vm.AllTracks, new WindowsSafeNamePolicy(), Vm.HistoryStore);
        await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.Rename"), 780, 640);
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
        if (await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.TagSearch"), 940, 640) is true &&
            dialog.Selected is { } candidate)
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
        if (await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.Lyrics"), 860, 680) is true)
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
        if (await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.Cover"), 820, 600) is not true) return;

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
        await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.History"), 820, 560);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new SettingsDialog(Vm.Settings, Vm.Search.Sources);
        if (await ModalHost.ShowAsync(Top, dialog, Localizer.T("Dialog.Settings"), 760, 840) is true)
            Vm.SaveSettings();
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app) return;

        var next = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = next;

        if (Vm is { } vm)
        {
            vm.Settings.Ui.Theme = next == ThemeVariant.Dark ? "Dark" : "Light";
            vm.SaveSettings();
        }
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dialog = new InfoDialog(Localizer.T("App.AboutTitle"), AboutContent.Create(Top, android: true));
        await ModalHost.ShowAsync(Top, dialog, Localizer.T("App.AboutTitle"), 480, 500);
    }

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) => await _updates.ShowManualAsync(Top);
}
