namespace Melotag.UI;

/// <summary>产品标识与版本号集中在这里；打包用的 csproj 版本需与之保持一致。</summary>
public static class AppInfo
{
    public const string Version = "1.1.0";
    public const string DisplayName = "音签 Melotag";
    public const string ShortName = "音签";

    public static string VersionLine(string suffix = "") =>
        string.IsNullOrEmpty(suffix) ? $"{ShortName} {Version}" : $"{ShortName} {Version} · {suffix}";
}
