namespace Melotag.Desktop;

using Avalonia;
using Melotag.UI.Platform;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 命令行传入的音频文件/目录，启动后自动加载（也支持"打开方式"）
        UI.App.StartupPaths = args;
        // 指纹解码是平台差异（Windows Media Foundation / Linux GStreamer），必须在 App 装配源之前注入。
        PlatformServices.Fingerprinter = new Audio.DesktopFingerprinter();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<UI.App>()
            .UsePlatformDetect()
            .LogToTrace();
}
