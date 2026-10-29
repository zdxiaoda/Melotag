namespace Melotag.UI;

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Melotag.Storage;
using Melotag.Tags;
using Melotag.UI.ViewModels;
using Melotag.UI.Views;

/// <summary>
/// 组合根：桌面（Classic）与 Android（单视图）共用同一份服务装配。
/// 平台差异只通过 Melotag.UI.Platform.PlatformServices 注入。
/// </summary>
public partial class App : Application
{
    /// <summary>桌面"打开方式"传入的路径，由 Program 在启动前设置。</summary>
    public static IReadOnlyList<string> StartupPaths { get; set; } = [];

    public MainWindowViewModel? ViewModel { get; private set; }

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
            tagStore, searchService, coverService, historyStore, settingsStore, settings,
            Platform.PlatformServices.Host);
        ViewModel = viewModel;

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = viewModel };
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                break;

            case ISingleViewApplicationLifetime single:
                single.MainView = new MobileShellView { DataContext = viewModel };
                break;
        }

        if (StartupPaths.Count > 0)
            _ = viewModel.AddPathsAsync(StartupPaths);

        base.OnFrameworkInitializationCompleted();
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
