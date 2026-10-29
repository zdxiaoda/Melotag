namespace Melotag.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Melotag.Core.Merge;
using Melotag.Core.Rename;
using Melotag.UI.Dialogs;
using Melotag.UI.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

        var files = await Platform.PlatformServices.FilePicker.PickFolderAsync(this);
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
            Vm.StatusText = "列表为空";
            return;
        }

        var dialog = new RenameDialog(Vm.AllTracks, new WindowsSafeNamePolicy(), Vm.HistoryStore);
        await ModalHost.ShowAsync(this, dialog, "重命名 / 文件名相关", 780, 640);
    }

    private async void OnAutoMatch(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.AutoMatchAllAsync();
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
            Vm.StatusText = "请先选择一个文件";
            return;
        }

        var dialog = new TagSearchDialog(Vm.Search, Vm.Settings, row);
        if (await ModalHost.ShowAsync(this, dialog, "在线标签匹配", 940, 640) is true && dialog.Selected is { } candidate)
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
        if (await ModalHost.ShowAsync(this, dialog, "歌词", 860, 680) is true)
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
        if (await ModalHost.ShowAsync(this, dialog, "封面", 820, 600) is not true) return;

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
        await ModalHost.ShowAsync(this, dialog, "标签历史", 820, 560);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new SettingsDialog(Vm.Settings, Vm.Search.Sources);
        if (await ModalHost.ShowAsync(this, dialog, "设置", 760, 840) is true)
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
        var logo = new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.AssetLoader.Open(new Uri("avares://Melotag.UI/Assets/melotag-logo.png"))),
            Width = 72,
            Height = 72,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        var text = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Text = "音签 Melotag 1.0.0\n\n跨平台音乐标签编辑器\n\n标签引擎：TagLib#\n图像处理：SkiaSharp\n界面：Avalonia + FluentAvalonia\n在线源：网易云音乐 / QQ 音乐 / 酷狗 / 酷我 / MusicBrainz / Cover Art Archive / iTunes / Last.fm / LRCLIB\n\n独立实现，不复用原软件的任何二进制或素材。",
        };

        var content = new Grid
        {
            Margin = new Thickness(20),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        logo.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(text, 1);
        content.Children.Add(logo);
        content.Children.Add(text);

        var dialog = new Window
        {
            Title = "关于 音签 Melotag",
            Icon = Icon,
            Width = 480,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content,
        };

        await dialog.ShowDialog(this);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Vm?.SaveSettings();
        base.OnClosed(e);
    }
}
