namespace Melotag.Core.Rename;

using System.Text.RegularExpressions;
using Melotag.Core.Models;

/// <summary>正则模式：第 N 个捕获组映射到某个字段。用于"从文件名反写标签"。</summary>
public sealed class RegexRenamePlan
{
    private RegexRenamePlan(Regex pattern, IReadOnlyDictionary<int, FieldKey> groupMap)
    {
        Pattern = pattern;
        GroupMap = groupMap;
    }

    public Regex Pattern { get; }
    public IReadOnlyDictionary<int, FieldKey> GroupMap { get; }

    public static RegexRenamePlan Create(string pattern, IReadOnlyDictionary<int, FieldKey> groupMap)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("正则不能为空", nameof(pattern));
        if (groupMap.Count == 0)
            throw new ArgumentException("至少映射一个捕获组", nameof(groupMap));
        if (groupMap.Keys.Any(k => k <= 0))
            throw new ArgumentException("捕获组编号必须从 1 开始", nameof(groupMap));
        if (groupMap.Keys.Distinct().Count() != groupMap.Count)
            throw new ArgumentException("捕获组编号不能重复", nameof(groupMap));

        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var maxGroup = regex.GetGroupNumbers().DefaultIfEmpty(0).Max();
        if (groupMap.Keys.Any(k => k > maxGroup))
            throw new ArgumentException($"映射了不存在的捕获组（最大 {maxGroup}）", nameof(groupMap));

        return new RegexRenamePlan(regex, new Dictionary<int, FieldKey>(groupMap));
    }

    /// <summary>返回仅包含被映射字段的标签集合；不匹配返回 null。</summary>
    public TrackTags? Map(string input)
    {
        var match = Pattern.Match(input);
        if (!match.Success) return null;

        var result = new TrackTags();
        foreach (var (group, field) in GroupMap)
        {
            if (group >= match.Groups.Count || !match.Groups[group].Success) continue;
            var value = match.Groups[group].Value.Trim();
            if (value.Length == 0) continue;

            switch (field)
            {
                case FieldKey.Title: result.Title = value; break;
                case FieldKey.Artist: result.Artist = value; break;
                case FieldKey.Album: result.Album = value; break;
                case FieldKey.AlbumArtist: result.AlbumArtist = value; break;
                case FieldKey.Genre: result.Genre = value; break;
                case FieldKey.Comment: result.Comment = value; break;
                case FieldKey.Composer: result.Composer = value; break;
                case FieldKey.Lyricist: result.Lyricist = value; break;
                case FieldKey.Lyrics: result.Lyrics = value; break;
                case FieldKey.Year when uint.TryParse(value, out var year): result.Year = year; break;
                case FieldKey.Track when uint.TryParse(value, out var track): result.Track = track; break;
                case FieldKey.Disc when uint.TryParse(value, out var disc): result.Disc = disc; break;
            }
        }

        return result;
    }
}
