namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using Melotag.UI.Localization;

public partial class HistoryDialog : ModalView
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
        HistoryList.ItemsSource = rows;
        if (rows.Count > 0) HistoryList.SelectedItem = rows[0];
        Status.Text = rows.Count == 0
            ? Localizer.T("History.NoRecords")
            : Localizer.F("History.Count", rows.Count);
    }

    private void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow row)
        {
            Status.Text = Localizer.T("History.SelectRecord");
            return;
        }

        _restore?.Invoke(row.Entry);
        Status.Text = Localizer.T("History.Restored");
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        _store.ClearAll();
        Reload();
        Status.Text = Localizer.T("History.Cleared");
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
