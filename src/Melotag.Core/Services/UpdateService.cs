namespace Melotag.Core.Services;

using System.Text.Json;
using Melotag.Core.Sources;

/// <summary>GitHub Releases 上的最新版本信息。</summary>
public sealed record ReleaseInfo(string Version, string Url, string? Notes, bool IsNewer);

/// <summary>查询 GitHub 仓库的最新 Release；网络/限流失败时静默返回 null。</summary>
public sealed class UpdateService
{
    public async Task<ReleaseInfo?> CheckLatestAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(ProductInfo.ReleasesApi, ct, accept: "application/vnd.github+json");
            return Parse(json, currentVersion);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public static ReleaseInfo? Parse(string json, string currentVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return null;

            var notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString()
                : null;

            var version = Normalize(tag);
            return new ReleaseInfo(version, url!, notes, CompareVersion(version, currentVersion) > 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int CompareVersion(string left, string right)
    {
        if (Version.TryParse(Normalize(left), out var a) && Version.TryParse(Normalize(right), out var b))
            return a.CompareTo(b);

        return string.CompareOrdinal(Normalize(left), Normalize(right));
    }

    private static string Normalize(string version)
    {
        var value = version.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        var cut = value.IndexOfAny(['-', '+']);
        return cut >= 0 ? value[..cut] : value;
    }
}
