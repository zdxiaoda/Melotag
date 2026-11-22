namespace Melotag.Core.Services;

using System.Globalization;
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

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return null;

            var notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString()
                : null;

            var version = Normalize(tag);
            return new ReleaseInfo(version, url, notes, CompareVersion(version, currentVersion) > 0, tag);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按 <c>.</c> 分段逐段数值比较，缺段补 0。任一侧无法解析成数字时返回 0（视为「没有更新」），
    /// 而不是退回字符串比较——string.CompareOrdinal 会把 "1.10.0" 判成小于 "1.9.0"。
    /// </summary>
    public static int CompareVersion(string left, string right)
    {
        var a = ParseSegments(Changelog.NormalizeVersion(left));
        var b = ParseSegments(Changelog.NormalizeVersion(right));
        if (a is null || b is null) return 0;

        var length = Math.Max(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }

    private static int[]? ParseSegments(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var segments = version.Split('.', StringSplitOptions.TrimEntries);
        var numbers = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return null;
            numbers[i] = number;
        }

        return numbers;
    }

    private static string Normalize(string version) => Changelog.NormalizeVersion(version);
}
