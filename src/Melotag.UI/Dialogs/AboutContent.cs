namespace Melotag.UI.Dialogs;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Melotag.UI.Localization;
using Melotag.UI.Views;

/// <summary>桌面与 Android 共用的“关于”内容，避免两套文案。</summary>
public static class AboutContent
{
    private static readonly string[] SourceIds =
    [
        "netease", "qq", "kugou", "kuwo", "musicbrainz", "coverart", "itunes", "lastfm", "lrclib",
    ];

    public static Control Create(TopLevel? owner, bool android = false)
    {
        var stack = new StackPanel { Spacing = 10 };

        stack.Children.Add(new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.AssetLoader.Open(new Uri("avares://Melotag.UI/Assets/melotag-logo.png"))),
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"{AppInfo.DisplayName} {AppInfo.Version}",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
        });

        stack.Children.Add(new TextBlock
        {
            Text = Localizer.T("App.Description"),
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = Localizer.T("About.Features"),
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = Localizer.T("About.Formats"),
            TextWrapping = TextWrapping.Wrap,
        });

        var sources = string.Join(" / ", SourceIds.Select(id => Localizer.SourceName(id, id)));
        stack.Children.Add(new TextBlock
        {
            Text = Localizer.F("App.SourcesLine", sources),
            TextWrapping = TextWrapping.Wrap,
        });

        if (android)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Localizer.T("App.AndroidNote"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = AppInfo.RepositoryUrl,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };

        var repository = new Button { Content = Localizer.T("About.Repository") };
        repository.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(repository) ?? owner;
            if (top?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri(AppInfo.RepositoryUrl));
        };
        buttons.Children.Add(repository);

        var checkUpdates = new Button { Content = Localizer.T("About.CheckUpdates") };
        checkUpdates.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(checkUpdates) ?? owner;
            await new UpdateUi().ShowManualAsync(top);
        };
        buttons.Children.Add(checkUpdates);

        stack.Children.Add(buttons);
        return new ScrollViewer { Content = stack };
    }
}
