namespace Melotag.Core.Rename;

using System.Text;
using Melotag.Core.Models;

public enum PatternDirection
{
    /// <summary>用标签生成文件名：允许相邻占位（如 @4@5）。</summary>
    Generate,

    /// <summary>从文件名反推标签：相邻/重复占位会产生歧义，禁止。</summary>
    Parse,
}

public enum PatternError
{
    Empty,
    NoPlaceholder,
    OnlyUnused,          // 只出现 @0
    DuplicatePlaceholder,
    AdjacentPlaceholders,
    UnknownPlaceholder,
}

public static class RenamePresets
{
    public static readonly IReadOnlyList<string> Defaults =
    [
        "@5. @1",
        "@4@5. @1",
        "@5. @2 - @1",
        "@4@5. @2 - @1",
        "@2 - @1",
        "@1 - @2",
    ];

    public static string Describe(char placeholder) => placeholder switch
    {
        '0' => "不使用",
        '1' => "标题",
        '2' => "艺术家",
        '3' => "专辑",
        '4' => "碟号",
        '5' => "音轨",
        '6' => "年份",
        '7' => "注释",
        '8' => "专辑艺术家",
        _ => "未知",
    };
}

/// <summary>@0–@8 占位符模板。</summary>
public static class NamePattern
{
    public static IReadOnlyList<PatternError> Validate(string? pattern, PatternDirection direction = PatternDirection.Generate)
    {
        var errors = new List<PatternError>();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errors.Add(PatternError.Empty);
            return errors;
        }

        var used = new List<char>();
        var adjacent = false;
        char? previous = null;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '@' || i + 1 >= pattern.Length || !char.IsAsciiDigit(pattern[i + 1]))
                continue;

            var digit = pattern[i + 1];
            if (digit > '8')
            {
                errors.Add(PatternError.UnknownPlaceholder);
                i++;
                previous = null;
                continue;
            }

            if (previous is not null)
                adjacent = true;

            used.Add(digit);
            previous = digit;
            i++;
        }

        if (used.Count == 0)
        {
            errors.Add(PatternError.NoPlaceholder);
            return errors;
        }

        if (used.All(c => c == '0'))
            errors.Add(PatternError.OnlyUnused);

        if (used.Distinct().Count() != used.Count)
            errors.Add(PatternError.DuplicatePlaceholder);

        if (direction == PatternDirection.Parse && adjacent)
            errors.Add(PatternError.AdjacentPlaceholders);

        return errors;
    }

    public static string? ValueOf(char placeholder, TrackTags tags) => placeholder switch
    {
        '0' => null,
        '1' => tags.Title,
        '2' => tags.Artist,
        '3' => tags.Album,
        '4' => tags.Disc?.ToString(),
        '5' => tags.Track?.ToString("00"),
        '6' => tags.Year?.ToString(),
        '7' => tags.Comment,
        '8' => tags.AlbumArtist,
        _ => null,
    };

    /// <summary>渲染文件名（不含扩展名）。空值按 keepPlaceholderWhenEmpty 决定输出空串还是原占位。</summary>
    public static string Render(string pattern, TrackTags tags, bool keepPlaceholderWhenEmpty = false)
    {
        var sb = new StringBuilder(pattern.Length + 32);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '@' && i + 1 < pattern.Length && char.IsAsciiDigit(pattern[i + 1]) && pattern[i + 1] <= '8')
            {
                var key = pattern[i + 1];
                var value = ValueOf(key, tags);
                if (string.IsNullOrEmpty(value))
                    sb.Append(keepPlaceholderWhenEmpty ? $"@{key}" : string.Empty);
                else
                    sb.Append(value);
                i++;
            }
            else
            {
                sb.Append(pattern[i]);
            }
        }

        return sb.ToString();
    }
}
