namespace Melotag.UI.Dialogs;

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Lyrics;
using Melotag.Core.Sources;
using Melotag.Storage;
using Melotag.UI.Localization;

public partial class SettingsDialog : ModalView
{
    public sealed record LanguageOption(string Code, string Label);
    public sealed record CountryOption(string Code, string Display);

    private readonly AppSettings _settings;
    private readonly Action? _onSaved;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = new AppSettings();
    }

    public SettingsDialog(AppSettings settings, IReadOnlyList<ITagSource>? sources = null, Action? onSaved = null)
    {
        _settings = settings;
        _onSaved = onSaved;

        InitializeComponent();

        ThreadsBox.Value = settings.Search.Threads;
        ItemsBox.Value = settings.Search.ItemsPerSource;
        AutoThreadsBox.Value = settings.Search.AutoMatchThreads;

        LastFmKeyBox.Text = settings.Sources.LastFmApiKey;
        UserAgentBox.Text = settings.Sources.UserAgent;

        var countryCode = string.IsNullOrWhiteSpace(settings.Sources.ITunesCountry)
            ? "CN"
            : settings.Sources.ITunesCountry.Trim().ToUpperInvariant();
        var countries = CreateCountryOptions(countryCode);
        CountryBox.ItemsSource = countries;
        CountryBox.SelectedItem = countries.FirstOrDefault(o =>
            string.Equals(o.Code, countryCode, StringComparison.OrdinalIgnoreCase)) ?? countries[0];

        sources ??= [];
        TagSources.Load(sources, settings.Sources.Tags, SourceDefaults.Tags);
        CoverSources.Load(sources, settings.Sources.Pictures, SourceDefaults.Pictures);
        LyricSources.Load(sources, settings.Sources.Lyrics, SourceDefaults.Lyrics);

        ReformatBox.IsChecked = settings.Lyrics.ReformatTimeTags;
        RemoveTagsBox.IsChecked = settings.Lyrics.RemoveTimeTags;
        BlankBox.IsChecked = settings.Lyrics.DeleteBlankLines;
        HeadBox.IsChecked = settings.Lyrics.DeleteHeadTags;
        InstrumentBox.IsChecked = settings.Lyrics.DontDownloadLyricWithInstrumentInTitle;
        EncodingBox.ItemsSource = TextFile.EncodingChoices;
        EncodingBox.SelectedItem = settings.Lyrics.Encoding;
        SaveDirBox.Text = settings.Lyrics.SaveDirectory;
        LrcNameBox.Text = settings.Lyrics.FileNameFormat;

        OriginalCoverBox.IsChecked = settings.Pictures.KeepOriginal;
        PicSizeBox.Value = settings.Pictures.MaxSizeKb;
        PicWidthBox.Value = settings.Pictures.MaxWidth;
        PicHeightBox.Value = settings.Pictures.MaxHeight;
        UpdatePictureLimitsEnabled();

        Id3Box.ItemsSource = new[] { "2.3", "2.4" };
        Id3Box.SelectedItem = settings.Writing.Id3v2Version;
        KeepMtimeBox.IsChecked = settings.Writing.KeepModifiedTime;
        WriteLyricsBox.IsChecked = settings.Writing.WriteLyricsToTag;

        var languageOptions = new List<LanguageOption>
        {
            new("auto", Localizer.T("Language.Auto")),
            new("zh-Hans", Localizer.T("Language.Chinese")),
            new("ja", Localizer.T("Language.Japanese")),
            new("en", Localizer.T("Language.English")),
        };

        var currentLanguage = settings.Ui.Language is "zh-CHS" or "zh-CN" ? "zh-Hans" : settings.Ui.Language;
        LanguageBox.ItemsSource = languageOptions;
        LanguageBox.SelectedItem = languageOptions.FirstOrDefault(o =>
            string.Equals(o.Code, currentLanguage, StringComparison.OrdinalIgnoreCase)) ?? languageOptions[0];
        CheckUpdatesBox.IsChecked = settings.Ui.CheckForUpdatesOnStartup;

        CompactBox.IsChecked = settings.Ui.UseCompactRows;
    }

    private static List<CountryOption> CreateCountryOptions(string currentCode)
    {
        var options = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try
                {
                    return new RegionInfo(culture.Name);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            })
            .Where(region => region is not null && region.TwoLetterISORegionName.Length == 2)
            .GroupBy(region => region!.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()!)
            .Select(region => new CountryOption(
                region.TwoLetterISORegionName,
                $"{region.TwoLetterISORegionName} — {CountryName(region)}"))
            .OrderBy(option => option.Code, StringComparer.Ordinal)
            .ToList();

        if (options.All(option =>
                !string.Equals(option.Code, currentCode, StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, new CountryOption(currentCode, currentCode));

        return options;
    }

    private static string CountryName(RegionInfo region) =>
        !string.IsNullOrWhiteSpace(region.NativeName) ? region.NativeName :
        !string.IsNullOrWhiteSpace(region.EnglishName) ? region.EnglishName :
        region.TwoLetterISORegionName;

    private void OnOriginalCoverChanged(object? sender, RoutedEventArgs e) => UpdatePictureLimitsEnabled();

    /// <summary>保留原图时，缩放/体积上限不生效。</summary>
    private void UpdatePictureLimitsEnabled()
    {
        var enabled = OriginalCoverBox.IsChecked != true;
        PicSizeBox.IsEnabled = enabled;
        PicWidthBox.IsEnabled = enabled;
        PicHeightBox.IsEnabled = enabled;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _settings.Search.Threads = (int)(ThreadsBox.Value ?? 4);
        _settings.Search.ItemsPerSource = (int)(ItemsBox.Value ?? 10);
        _settings.Search.AutoMatchThreads = (int)(AutoThreadsBox.Value ?? 2);

        _settings.Sources.LastFmApiKey = LastFmKeyBox.Text?.Trim() ?? "";
        _settings.Sources.ITunesCountry = (CountryBox.SelectedItem as CountryOption)?.Code ?? "CN";
        _settings.Sources.UserAgent = UserAgentBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(_settings.Sources.UserAgent))
            Melotag.Core.Sources.Http.UserAgent = _settings.Sources.UserAgent;

        _settings.Sources.Tags = TagSources.GetOrderedIds();
        _settings.Sources.Pictures = CoverSources.GetOrderedIds();
        _settings.Sources.Lyrics = LyricSources.GetOrderedIds();

        _settings.Lyrics.ReformatTimeTags = ReformatBox.IsChecked == true;
        _settings.Lyrics.RemoveTimeTags = RemoveTagsBox.IsChecked == true;
        _settings.Lyrics.DeleteBlankLines = BlankBox.IsChecked == true;
        _settings.Lyrics.DeleteHeadTags = HeadBox.IsChecked == true;
        _settings.Lyrics.DontDownloadLyricWithInstrumentInTitle = InstrumentBox.IsChecked == true;
        _settings.Lyrics.Encoding = EncodingBox.SelectedItem as string ?? "utf-8";
        _settings.Lyrics.SaveDirectory = SaveDirBox.Text?.Trim() ?? "";
        _settings.Lyrics.FileNameFormat = LrcNameBox.Text?.Trim() ?? "";

        _settings.Pictures.KeepOriginal = OriginalCoverBox.IsChecked == true;
        _settings.Pictures.MaxSizeKb = (int)(PicSizeBox.Value ?? 1024);
        _settings.Pictures.MaxWidth = (int)(PicWidthBox.Value ?? 1200);
        _settings.Pictures.MaxHeight = (int)(PicHeightBox.Value ?? 1200);

        _settings.Writing.Id3v2Version = Id3Box.SelectedItem as string ?? "2.3";
        _settings.Writing.KeepModifiedTime = KeepMtimeBox.IsChecked == true;
        _settings.Writing.WriteLyricsToTag = WriteLyricsBox.IsChecked == true;

        _settings.Ui.UseCompactRows = CompactBox.IsChecked == true;

        var language = (LanguageBox.SelectedItem as LanguageOption)?.Code ?? "auto";
        _settings.Ui.Language = language;
        Localizer.Instance.SetLanguage(language);
        _settings.Ui.CheckForUpdatesOnStartup = CheckUpdatesBox.IsChecked == true;

        _onSaved?.Invoke();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
