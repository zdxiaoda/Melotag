namespace Melotag.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.Platform;
using Melotag.UI.ViewModels;

/// <summary>
/// Android（单视图）主界面：一个列表页 + 一个详情页，对话框全部走 ModalHost 浮层。
/// </summary>
public partial class MobileShellView : UserControl
{
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
            Vm.StatusText = "当前平台不支持选择文件夹，请用「添加文件」多选";
            return;
        }

        var files = await PlatformServices.FilePicker.PickFolderAsync(top);
        if (files.Count > 0) await Vm.AddFilesAsync(files);
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
        if (Vm is not null) await Vm.AutoMatchAllAsync();
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

        if (!Vm.Platform.SupportsRename)
        {
            Vm.StatusText = "Android 版暂不支持重命名文件，请在电脑端处理";
            return;
        }

        if (Vm.AllTracks.Count == 0)
        {
            Vm.StatusText = "列表为空";
            return;
        }

        var dialog = new RenameDialog(Vm.AllTracks, new WindowsSafeNamePolicy(), Vm.HistoryStore);
        await ModalHost.ShowAsync(Top, dialog, "重命名 / 文件名相关", 780, 640);
    }

    private async void OnTagSearch(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = "请先选择一个文件";
            return;
        }

        var dialog = new TagSearchDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(Top, dialog, "在线标签匹配", 940, 640) is true &&
            dialog.Selected is { } candidate)
            await Vm.ApplyCandidateAsync(row, candidate, dialog.Rule);
    }

    private async void OnLyrics(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = "请先选择一个文件";
            return;
        }

        var dialog = new LyricsDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(Top, dialog, "歌词", 860, 680) is true)
        {
            row.Lyrics = dialog.ResultLyrics;
            Vm.StatusText = "歌词已更新（保存标签后写入文件）";
        }
    }

    private async void OnCover(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = "请先选择一个文件";
            return;
        }

        var dialog = new CoverDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(Top, dialog, "封面", 820, 600) is not true) return;

        if (dialog.Removed)
        {
            row.ClearCover();
            Vm.StatusText = "封面已移除（保存后写入文件）";
        }
        else if (dialog.Result is { } data)
        {
            row.SetCover(data, dialog.ResultMime);
            Vm.StatusText = "封面已更新（保存后写入文件）";
        }
    }

    private async void OnHistory(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.SelectedTrack is not { } row)
        {
            Vm.StatusText = "请先选择一个文件";
            return;
        }

        var dialog = new HistoryDialog(Vm.HistoryStore, row.Key,
            entry => _ = Vm.RestoreHistoryAsync(row, entry));
        await ModalHost.ShowAsync(Top, dialog, "标签历史", 820, 560);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new SettingsDialog(Vm.Settings, Vm.Search.Sources);
        if (await ModalHost.ShowAsync(Top, dialog, "设置", 760, 840) is true)
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
        var text = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = $"{AppInfo.DisplayName} {AppInfo.Version}（Android）\n\n跨平台音乐标签编辑器\n\n标签引擎：TagLib#\n图像处理：SkiaSharp\n界面：Avalonia + FluentAvalonia\n在线源：网易云音乐 / QQ 音乐 / 酷狗 / 酷我 / MusicBrainz / Cover Art Archive / iTunes / Last.fm / LRCLIB\n\n文件通过系统 SAF 授权，标签写回原文件。",
        };

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(4) };
        content.Children.Add(new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.AssetLoader.Open(new Uri("avares://Melotag.UI/Assets/melotag-logo.png"))),
            Width = 64,
            Height = 64,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        });
        content.Children.Add(text);

        var dialog = new InfoDialog("关于 音签 Melotag", content);
        await ModalHost.ShowAsync(Top, dialog, "关于 音签 Melotag", 460, 380);
    }
}
