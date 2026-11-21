namespace Melotag.UI.Dialogs;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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

    /// <summary>供宿主（Escape、窗口关闭等）触发关闭。</summary>
    public void RequestClose(object? result = null) => Close(result);

    /// <summary>
    /// 窄屏（手机）下给自身切换 <c>narrow</c> 类，供 XAML 里
    /// <c>UserControl.narrow</c> 前缀的样式收紧布局。
    /// </summary>
    protected void HookNarrowMode() =>
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width is > 0 and < ModalHost.NarrowWidth;
            if (narrow == Classes.Contains("narrow")) return;

            if (narrow) Classes.Add("narrow");
            else Classes.Remove("narrow");
        };
}

public static class ModalHost
{
    /// <summary>窄于此宽度按手机布局处理（浮层全出血、对话框启用 narrow 样式）。</summary>
    internal const double NarrowWidth = 600;

    private static readonly List<ModalView> Overlays = [];

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
            ShowOverlay(owner, view, width, height, completion);
        else
            ShowWindow(window, view, title, width, height, completion);

        return completion.Task;
    }

    /// <summary>
    /// Android 等单视图平台：居中显示为卡片，尺寸取"请求尺寸"与"屏幕减去边距"的较小值。
    /// </summary>
    private static void ShowOverlay(TopLevel? owner, ModalView view, double requestedWidth,
        double requestedHeight, TaskCompletionSource<object?> completion)
    {
        if (owner is null || OverlayLayer.GetOverlayLayer(owner) is not { } layer)
        {
            completion.TrySetResult(null);
            return;
        }

        view.ShowHeader = true;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
        };

        var host = new Grid
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        host.Children.Add(backdrop);
        host.Children.Add(view);
        layer.Children.Add(host);
        Overlays.Add(view);

        void ApplySize()
        {
            var area = owner.Bounds.Size;
            var safe = owner.InsetsManager?.SafeAreaPadding ?? default;

            // 窄屏（手机）铺满安全区，桌面浮层保留 24px 边距。
            var outer = area.Width < NarrowWidth ? 0 : 24;
            var width = Math.Min(requestedWidth,
                Math.Max(280, area.Width - safe.Left - safe.Right - outer));
            var height = Math.Min(requestedHeight,
                Math.Max(200, area.Height - safe.Top - safe.Bottom - outer));

            host.Width = area.Width;
            host.Height = area.Height;
            view.Margin = safe;
            view.Width = width;
            view.Height = height;
        }

        ApplySize();
        owner.SizeChanged += OnOwnerSizeChanged;
        owner.InsetsManager?.SafeAreaChanged += OnInsetsChanged;

        _ = completion.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            owner.SizeChanged -= OnOwnerSizeChanged;
            owner.InsetsManager?.SafeAreaChanged -= OnInsetsChanged;
            Overlays.Remove(view);
            layer.Children.Remove(host);
            host.Children.Clear();
        }), TaskScheduler.Default);

        void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e) => ApplySize();
        void OnInsetsChanged(object? sender, EventArgs e) => ApplySize();
    }

    /// <summary>
    /// 关闭最上层浮层（Android 返回键等场景）。没有浮层时返回 false，
    /// 由调用方继续处理（如返回详情页 / 交给系统默认行为）。
    /// </summary>
    public static bool CloseTopmost()
    {
        if (Overlays.Count == 0) return false;

        Overlays[^1].RequestClose();
        return true;
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

        var closed = false;

        void OnCloseRequested(object? result)
        {
            if (closed) return;
            closed = true;
            window.Close();
        }

        view.CloseRequested += OnCloseRequested;
        window.Closed += (_, _) =>
        {
            closed = true;
            view.CloseRequested -= OnCloseRequested;
            completion.TrySetResult(view.CloseResult);
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            view.RequestClose(false);
            e.Handled = true;
        };

        _ = window.ShowDialog(owner);
    }
}
