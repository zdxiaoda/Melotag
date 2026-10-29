namespace Melotag.Desktop;

using Avalonia;

internal static class Program
{
    /// <summary>命令行传入的音频文件/目录，启动后自动加载（也支持"打开方式"）。</summary>
    public static string[] StartupPaths { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupPaths = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
