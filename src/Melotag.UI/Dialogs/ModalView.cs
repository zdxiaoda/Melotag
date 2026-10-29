namespace Melotag.UI.Dialogs;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// 模态内容的基类：内容是 UserControl，桌面由 ModalHost 包成窗口，
/// Android（单视图生命周期）没有 Window，改为覆盖层显示。
/// </summary>
public abstract class ModalView : UserControl
{
    public event Action<object?>? CloseRequested;

    /// <summary>关闭结果，供宿主在取消/返回键时读取。</summary>
    public object? CloseResult { get; private set; }

    /// <summary>桌面窗口自带标题栏，卡内标题栏只在浮层模式下显示。</summary>
    public bool ShowHeader
    {
        get => this.FindControl<Border>("HeaderBar")?.IsVisible ?? false;
        set
        {
            if (this.FindControl<Border>("HeaderBar") is { } header)
                header.IsVisible = value;
        }
    }

    protected void Close(object? result = null)
    {
        CloseResult = result;
        CloseRequested?.Invoke(result);
    }
}

public static class ModalHost
{
    private static bool IsSingleView =>
        Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime;

    /// <summary>显示模态内容，返回关闭结果。</summary>
    public static Task<object?> ShowAsync(TopLevel? owner, ModalView view, string title, double width = 760, double height = 680)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClose(object? result)
        {
            view.CloseRequested -= OnClose;
            completion.TrySetResult(result);
        }

        view.CloseRequested += OnClose;

        if (IsSingleView || owner is not Window window)
            ShowOverlay(owner, view, completion);
        else
            ShowWindow(window, view, title, width, height, completion);

        return completion.Task;
    }

    private static void ShowOverlay(TopLevel? owner, ModalView view, TaskCompletionSource<object?> completion)
    {
        if (owner is null || OverlayLayer.GetOverlayLayer(owner) is not { } layer) return;

        view.ShowHeader = true;
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
        view.Margin = new Thickness(10);

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
        };

        var host = new Grid { Background = Brushes.Transparent };
        host.Children.Add(backdrop);
        host.Children.Add(view);
        layer.Children.Add(host);

        _ = completion.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            layer.Children.Remove(host);
            host.Children.Clear();
        }), TaskScheduler.Default);
    }

    private static void ShowWindow(Window owner, ModalView view, string title, double width, double height,
        TaskCompletionSource<object?> completion)
    {
        view.ShowHeader = false;

        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 520),
            MinHeight = Math.Min(height, 360),
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = view,
            },
        };

        window.Closed += (_, _) => completion.TrySetResult(view.CloseResult);
        _ = window.ShowDialog(owner).ContinueWith(_ => completion.TrySetResult(view.CloseResult), TaskScheduler.Default);
    }
}
