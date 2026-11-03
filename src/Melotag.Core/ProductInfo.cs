using System.Reflection;

namespace Melotag.Core;

/// <summary>
/// 仓库地址、产品名与默认 User-Agent；Core / Storage / UI 共用，避免多处占位地址。
/// 版本号取自程序集（由 <c>Directory.Build.props</c> 的 <c>Version</c> 或发布流水线的
/// <c>-p:Version=...</c> 注入），升级版本时无需改动本文件。
/// </summary>
public static class ProductInfo
{
    public const string RepositoryUrl = "https://github.com/zdxiaoda/Melotag";
    public const string ReleasesApi = "https://api.github.com/repos/zdxiaoda/Melotag/releases/latest";

    public static string Version { get; } = ReadVersion();

    /// <summary>形如 <c>Melotag/1.3.2 (+https://github.com/zdxiaoda/Melotag)</c>，随版本自动变化。</summary>
    public static string UserAgent { get; } = $"Melotag/{Version} (+{RepositoryUrl})";

    private static string ReadVersion()
    {
        var informational = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 带源码链接时会附加 "+<commit>"，这里只保留语义版本部分。
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(ProductInfo).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
