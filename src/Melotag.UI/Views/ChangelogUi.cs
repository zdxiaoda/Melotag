namespace Melotag.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Melotag.Core;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

/// <summary>
/// 启动后弹一次当前版本的更新内容：升级和新装都会提示，记录在
/// <see cref="Melotag.Storage.UiSettings.LastSeenVersion"/>，同一版本不再重复。
/// </summary>
internal static class ChangelogUi
{
    private static bool _shown;

    public static async Task ShowIfNeededAsync(TopLevel? owner, MainWindowViewModel viewModel)
    {
        if (_shown || owner is null) return;
        _shown = true;

        var version = AppInfo.Version;
        if (string.Equals(viewModel.Settings.Ui.LastSeenVersion, version, StringComparison.OrdinalIgnoreCase))
            return;

        var notes = Changelog.ForVersion(version);
        if (string.IsNullOrWhiteSpace(notes))
        {
            MarkSeen(viewModel, version);
            return;
        }

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(ReleaseNotesView.Build(notes));
        content.Children.Add(BuildOpenReleaseButton(version));

        var title = Localizer.F("Update.WhatsNew", version);
        var dialog = new InfoDialog(title, content);
        await ModalHost.ShowAsync(owner, dialog, title, 560, 460);

        MarkSeen(viewModel, version);
    }

    private static Button BuildOpenReleaseButton(string version)
    {
        var button = new Button { Content = Localizer.T("Update.OpenRelease"), Classes = { "accent" } };
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(button)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri($"{ProductInfo.RepositoryUrl}/releases/tag/v{version}"));
        };
        return button;
    }

    private static void MarkSeen(MainWindowViewModel viewModel, string version)
    {
        viewModel.Settings.Ui.LastSeenVersion = version;
        try
        {
            // Android 不在退出时保存设置，这里立即落盘。
            viewModel.SaveSettings();
        }
        catch (Exception)
        {
            // 只读数据目录不应阻止启动，最多下次再提示。
        }
    }
}
