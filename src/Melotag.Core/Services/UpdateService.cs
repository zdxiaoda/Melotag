namespace Melotag.Core.Services;

using System.Text.Json;
using Melotag.Core.Sources;

/// <summary>GitHub Releases 上的最新版本信息。</summary>
public sealed record ReleaseInfo(string Version, string Url, string? Notes, bool IsNewer, string? Tag = null);

/// <summary>查询 GitHub 仓库的最新 Release；网络/限流失败时静默返回 null。</summary>
public sealed class UpdateService
{
    public async Task<ReleaseInfo?> CheckLatestAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(ProductInfo.ReleasesApi, ct, accept: "application/vnd.github+json");
            var release = Parse(json, currentVersion);
            if (release is null || !release.IsNewer) return release;

            // Release 说明由自动生成，通常没有内容；改为读取该 tag 上的 CHANGELOG.md。
            var notes = await FetchChangelogAsync(release.Tag ?? $"v{release.Version}", release.Version, ct);
            return string.IsNullOrWhiteSpace(notes) ? release : release with { Notes = notes };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> FetchChangelogAsync(string tag, string version, CancellationToken ct)
    {
        try
        {
            var markdown = await Http.GetStringAsync(
                ProductInfo.ChangelogApi(tag), ct, accept: "application/vnd.github.raw");
            return Changelog.ExtractSection(markdown, version);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // 读取失败时退回 Release body（通常为空），不影响版本判断。
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
            return new ReleaseInfo(version, url!, notes, CompareVersion(version, currentVersion) > 0, tag);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int CompareVersion(string left, string right)
    {
        if (Version.TryParse(Changelog.NormalizeVersion(left), out var a) &&
            Version.TryParse(Changelog.NormalizeVersion(right), out var b))
            return a.CompareTo(b);

        return string.CompareOrdinal(Changelog.NormalizeVersion(left), Changelog.NormalizeVersion(right));
    }

    private static string Normalize(string version) => Changelog.NormalizeVersion(version);
}
