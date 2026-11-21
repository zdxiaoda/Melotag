using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Melotag.UI.Platform;

namespace Melotag.Droid;

[Application]
public class MelotagAndroidApplication : AvaloniaAndroidApplication<Melotag.UI.App>
{
    protected MelotagAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        // 数据目录必须在 App 读取 settings.json / 打开 history.db 之前确定。
        PlatformServices.Host = new AndroidPlatformHost(this);

        // 指纹：MediaCodec 平台解码 + AcoustID.NET 纯托管指纹计算，无二进制依赖。
        PlatformServices.Fingerprinter = new Audio.AndroidFingerprinter(this);
    }

    public override void OnCreate()
    {
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder);
}
