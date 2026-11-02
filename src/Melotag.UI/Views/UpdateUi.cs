namespace Melotag.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Melotag.Core.Services;
using Melotag.UI.Dialogs;
using Melotag.UI.Localization;
using Melotag.UI.ViewModels;

/// <summary>GitHub Releases 更新检查：启动静默提示 + 手动检查对话框。</summary>
public sealed class UpdateUi
{
    private readonly UpdateService _service = new();

    public async Task CheckOnStartupAsync(MainWindowViewModel viewModel)
    {
        var info = await _service.CheckLatestAsync(AppInfo.Version, CancellationToken.None);
        if (info is not { IsNewer: true }) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
            viewModel.StatusText = Localizer.F("Update.AvailableStatus", info.Version));
    }

    public async Task ShowManualAsync(TopLevel? owner)
    {
        var info = await _service.CheckLatestAsync(AppInfo.Version, CancellationToken.None);

        var content = new StackPanel { Spacing = 10 };
        var message = info is null
            ? Localizer.T("Update.Failed")
            : info.IsNewer
                ? Localizer.F("Update.NewVersion", info.Version, AppInfo.Version)
                : Localizer.F("Update.Latest", AppInfo.Version);

        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        if (info is { IsNewer: true })
        {
            content.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(info.Notes) ? Localizer.T("Update.NoNotes") : info.Notes,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 220,
            });

            var open = new Button { Content = Localizer.T("Update.OpenRelease"), Classes = { "accent" } };
            open.Click += async (_, _) =>
            {
                var top = TopLevel.GetTopLevel(open) ?? owner;
                if (top?.Launcher is { } launcher)
                    await launcher.LaunchUriAsync(new Uri(info.Url));
            };
            content.Children.Add(open);
        }

        var dialog = new InfoDialog(Localizer.T("Menu.Help.CheckUpdates"), content);
        await ModalHost.ShowAsync(owner, dialog, Localizer.T("Menu.Help.CheckUpdates"), 560, 420);
    }
}
