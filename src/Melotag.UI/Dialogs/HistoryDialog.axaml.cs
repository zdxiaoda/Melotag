namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;

public partial class HistoryDialog : Window
{
    private sealed record HistoryRow(HistoryEntry Entry)
    {
        public string CreatedText => Entry.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        public string Title => Entry.Tags.Title ?? "";
        public string Artist => Entry.Tags.Artist ?? "";
        public string Album => Entry.Tags.Album ?? "";
        public string Serial => Entry.Serial;
    }

    private readonly IHistoryStore? _store;
    private readonly string? _path;
    private readonly Action<HistoryEntry>? _restore;

    public HistoryDialog()
    {
        InitializeComponent();
    }

    public HistoryDialog(IHistoryStore store, string path, Action<HistoryEntry> restore)
    {
        _store = store;
        _path = path;
        _restore = restore;

        InitializeComponent();

        Header.Text = path;
        Reload();
    }

    private void Reload()
    {
        if (_store is null || _path is null) return;

        var entries = _store.GetHistory(_path);
        var rows = entries.Select(e => new HistoryRow(e)).ToList();
        HistoryGrid.ItemsSource = rows;
        if (rows.Count > 0) HistoryGrid.SelectedItem = rows[0];
        Status.Text = rows.Count == 0 ? "该文件还没有历史记录" : $"共 {rows.Count} 条记录";
    }

    private void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
        {
            Status.Text = "请先选择一条记录";
            return;
        }

        _restore?.Invoke(row.Entry);
        Status.Text = "已恢复到所选记录（标签将在下次保存时写入文件）";
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        _store.ClearAll();
        Reload();
        Status.Text = "历史已清空";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
