namespace Melotag.Core;

/// <summary>仓库地址与默认 User-Agent；Core / Storage / UI 共用，避免多处占位地址。</summary>
public static class ProductInfo
{
    public const string RepositoryUrl = "https://github.com/zdxiaoda/Melotag";
    public const string ReleasesApi = "https://api.github.com/repos/zdxiaoda/Melotag/releases/latest";
    public const string UserAgent = "Melotag/1.2.1 (+https://github.com/zdxiaoda/Melotag)";
}
