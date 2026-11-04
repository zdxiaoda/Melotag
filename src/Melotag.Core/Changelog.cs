namespace Melotag.Core;

/// <summary>
/// 仓库根目录的 <c>CHANGELOG.md</c>：按版本截取段落，供更新检查与升级提示复用。
/// 文件以嵌入资源打包，离线也能读取当前版本的变化。
/// </summary>
public static class Changelog
{
    private const string ResourceName = "Melotag.Core.CHANGELOG.md";

    private static readonly Lazy<string?> Embedded = new(ReadEmbedded, isThreadSafe: true);

    /// <summary>取嵌入的 CHANGELOG.md 中该版本的段落；没有该版本时返回 null。</summary>
    public static string? ForVersion(string version) =>
        Embedded.Value is { } markdown ? ExtractSection(markdown, version) : null;

    /// <summary>
    /// 从 Markdown 中截取 <c>## &lt;version&gt;</c> 到下一个二级标题之间的内容。
    /// 兼容 <c>## 1.3.2</c>、<c>## v1.3.2</c>、<c>## [1.3.2] - 2026-01-01</c>。
    /// </summary>
    public static string? ExtractSection(string markdown, string version)
    {
        var wanted = NormalizeVersion(version);
        if (wanted.Length == 0) return null;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var start = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!TryReadHeading(lines[i], out var label)) continue;

            if (start < 0)
            {
                if (NormalizeVersion(label) == wanted) start = i + 1;
                continue;
            }

            return Collect(lines, start, i);
        }

        return start < 0 ? null : Collect(lines, start, lines.Length);
    }

    /// <summary>去空格、去前导 <c>v</c>，并截断 <c>-</c>/<c>+</c> 之后的后缀。</summary>
    public static string NormalizeVersion(string version)
    {
        var value = version.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        var cut = value.IndexOfAny(['-', '+']);
        return cut >= 0 ? value[..cut].Trim() : value;
    }

    private static string? Collect(string[] lines, int start, int end)
    {
        var section = string.Join('\n', lines[start..end]).Trim();
        return section.Length == 0 ? null : section;
    }

    private static bool TryReadHeading(string line, out string label)
    {
        label = "";
        var trimmed = line.TrimEnd();
        if (!trimmed.StartsWith("## ", StringComparison.Ordinal)) return false;

        var text = trimmed[3..].Trim();
        if (text.Length == 0) return false;

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close <= 1) return false;
            label = text[1..close];
            return true;
        }

        var space = text.IndexOfAny([' ', '\t']);
        label = space >= 0 ? text[..space] : text;
        return true;
    }

    private static string? ReadEmbedded()
    {
        try
        {
            using var stream = typeof(Changelog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
