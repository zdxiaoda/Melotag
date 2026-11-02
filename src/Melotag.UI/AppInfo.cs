namespace Melotag.UI;

/// <summary>产品标识与版本号集中在这里；打包用的 csproj 版本需与之保持一致。</summary>
public static class AppInfo
{
    public const string Version = "1.2.0";
    public const string RepositoryUrl = Melotag.Core.ProductInfo.RepositoryUrl;

    public static string ReleasesUrl => RepositoryUrl + "/releases";
    public static string DisplayName => Localization.Localizer.T("App.Name");
    public static string ShortName => Localization.Localizer.T("App.ShortName");

    public static string VersionLine(string suffix = "") =>
        string.IsNullOrEmpty(suffix) ? $"{ShortName} {Version}" : $"{ShortName} {Version} · {suffix}";
}
