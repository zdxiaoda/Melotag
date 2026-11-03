namespace Melotag.UI;

/// <summary>产品标识；版本号来自程序集版本，由 Directory.Build.props / 发布流水线注入。</summary>
public static class AppInfo
{
    public static string Version => Melotag.Core.ProductInfo.Version;
    public const string RepositoryUrl = Melotag.Core.ProductInfo.RepositoryUrl;

    public static string ReleasesUrl => RepositoryUrl + "/releases";
    public static string DisplayName => Localization.Localizer.T("App.Name");
    public static string ShortName => Localization.Localizer.T("App.ShortName");

    public static string VersionLine(string suffix = "") =>
        string.IsNullOrEmpty(suffix) ? $"{ShortName} {Version}" : $"{ShortName} {Version} · {suffix}";
}
