namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Lyrics;
using Melotag.Storage;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly Action? _onSaved;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = new AppSettings();
    }

    public SettingsDialog(AppSettings settings, Action? onSaved = null)
    {
        _settings = settings;
        _onSaved = onSaved;

        InitializeComponent();

        ThreadsBox.Value = settings.Search.Threads;
        ItemsBox.Value = settings.Search.ItemsPerSource;
        AutoThreadsBox.Value = settings.Search.AutoMatchThreads;

        LastFmKeyBox.Text = settings.Sources.LastFmApiKey;
        CountryBox.Text = settings.Sources.ITunesCountry;
        UserAgentBox.Text = settings.Sources.UserAgent;

        ReformatBox.IsChecked = settings.Lyrics.ReformatTimeTags;
        RemoveTagsBox.IsChecked = settings.Lyrics.RemoveTimeTags;
        BlankBox.IsChecked = settings.Lyrics.DeleteBlankLines;
        HeadBox.IsChecked = settings.Lyrics.DeleteHeadTags;
        InstrumentBox.IsChecked = settings.Lyrics.DontDownloadLyricWithInstrumentInTitle;
        EncodingBox.ItemsSource = TextFile.EncodingChoices;
        EncodingBox.SelectedItem = settings.Lyrics.Encoding;
        SaveDirBox.Text = settings.Lyrics.SaveDirectory;
        LrcNameBox.Text = settings.Lyrics.FileNameFormat;

        PicSizeBox.Value = settings.Pictures.MaxSizeKb;
        PicWidthBox.Value = settings.Pictures.MaxWidth;
        PicHeightBox.Value = settings.Pictures.MaxHeight;

        Id3Box.ItemsSource = new[] { "2.3", "2.4" };
        Id3Box.SelectedItem = settings.Writing.Id3v2Version;
        KeepMtimeBox.IsChecked = settings.Writing.KeepModifiedTime;
        WriteLyricsBox.IsChecked = settings.Writing.WriteLyricsToTag;

        CompactBox.IsChecked = settings.Ui.UseCompactRows;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _settings.Search.Threads = (int)(ThreadsBox.Value ?? 4);
        _settings.Search.ItemsPerSource = (int)(ItemsBox.Value ?? 10);
        _settings.Search.AutoMatchThreads = (int)(AutoThreadsBox.Value ?? 2);

        _settings.Sources.LastFmApiKey = LastFmKeyBox.Text?.Trim() ?? "";
        _settings.Sources.ITunesCountry = CountryBox.Text?.Trim() ?? "CN";
        _settings.Sources.UserAgent = UserAgentBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(_settings.Sources.UserAgent))
            Melotag.Core.Sources.Http.UserAgent = _settings.Sources.UserAgent;

        _settings.Lyrics.ReformatTimeTags = ReformatBox.IsChecked == true;
        _settings.Lyrics.RemoveTimeTags = RemoveTagsBox.IsChecked == true;
        _settings.Lyrics.DeleteBlankLines = BlankBox.IsChecked == true;
        _settings.Lyrics.DeleteHeadTags = HeadBox.IsChecked == true;
        _settings.Lyrics.DontDownloadLyricWithInstrumentInTitle = InstrumentBox.IsChecked == true;
        _settings.Lyrics.Encoding = EncodingBox.SelectedItem as string ?? "utf-8";
        _settings.Lyrics.SaveDirectory = SaveDirBox.Text?.Trim() ?? "";
        _settings.Lyrics.FileNameFormat = LrcNameBox.Text?.Trim() ?? "";

        _settings.Pictures.MaxSizeKb = (int)(PicSizeBox.Value ?? 1024);
        _settings.Pictures.MaxWidth = (int)(PicWidthBox.Value ?? 1200);
        _settings.Pictures.MaxHeight = (int)(PicHeightBox.Value ?? 1200);

        _settings.Writing.Id3v2Version = Id3Box.SelectedItem as string ?? "2.3";
        _settings.Writing.KeepModifiedTime = KeepMtimeBox.IsChecked == true;
        _settings.Writing.WriteLyricsToTag = WriteLyricsBox.IsChecked == true;

        _settings.Ui.UseCompactRows = CompactBox.IsChecked == true;

        _onSaved?.Invoke();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
