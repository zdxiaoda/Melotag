namespace Melotag.Desktop;

using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Melotag.Storage;
using Melotag.Tags;
using Melotag.UI.ViewModels;
using Melotag.UI.Views;

public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var settingsStore = new JsonSettingsStore();
        var settings = settingsStore.Load();

        RequestedThemeVariant = settings.Ui.Theme switch
        {
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };

        var tagStore = new TagLibTagStore();
        var historyStore = new SqliteHistoryStore();
        historyStore.Initialize();

        Core.Sources.Http.UserAgent = string.IsNullOrWhiteSpace(settings.Sources.UserAgent)
            ? Core.Sources.Http.UserAgent
            : settings.Sources.UserAgent;

        var searchService = new Core.Sources.TagSearchService(
        [
            new Core.Sources.NeteaseSource(),
            new Core.Sources.QQMusicSource(),
            new Core.Sources.KugouSource(),
            new Core.Sources.KuwoSource(),
            new Core.Sources.MusicBrainzSource(),
            new Core.Sources.CoverArtArchiveSource(),
            new Core.Sources.ITunesSource(() => settings.Sources.ITunesCountry),
            new Core.Sources.LastFmSource(() => settings.Sources.LastFmApiKey),
            new Core.Sources.LrclibSource(),
        ]);

        var coverService = new Core.Services.CoverService();

        var viewModel = new MainWindowViewModel(
            tagStore, searchService, coverService, historyStore, settingsStore, settings);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
        }

        var startupPaths = Program.StartupPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (startupPaths.Length > 0)
            _ = viewModel.AddPathsAsync(startupPaths);

        base.OnFrameworkInitializationCompleted();
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
