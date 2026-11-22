namespace Melotag.Core.Merge;

using Melotag.Core.Models;

public enum OverwriteRule
{
    KeepExisting,
    FillEmptyOnly,
    Replace,

    /// <summary>完全不修改该字段（批量匹配时用于用户未勾选的字段）。</summary>
    Ignore,
}

public sealed class OverwriteOptions
{
    public OverwriteRule Default { get; set; } = OverwriteRule.FillEmptyOnly;
    public Dictionary<FieldKey, OverwriteRule> PerField { get; } = [];

    /// <summary>
    /// 封面（Pictures）的覆盖规则。封面不是文本字段，故意不放进 <see cref="FieldKey"/>：
    /// 否则 UI 的字段枚举/勾选列表会多出一个不可编辑的项。
    /// 默认 <see cref="OverwriteRule.FillEmptyOnly"/>，即只在当前没有封面时补封面。
    /// </summary>
    public OverwriteRule PicturesRule { get; set; } = OverwriteRule.FillEmptyOnly;

    public OverwriteRule RuleFor(FieldKey key) =>
        PerField.TryGetValue(key, out var rule) ? rule : Default;
}

/// <summary>把在线/外部候选标签合并进当前标签。</summary>
public static class TagMerger
{
    public static TrackTags Merge(TrackTags current, TrackTags candidate, OverwriteOptions options)
    {
        var result = current.Clone();

        result.Title = Pick(result.Title, candidate.Title, options.RuleFor(FieldKey.Title));
        result.Artist = Pick(result.Artist, candidate.Artist, options.RuleFor(FieldKey.Artist));
        result.Album = Pick(result.Album, candidate.Album, options.RuleFor(FieldKey.Album));
        result.AlbumArtist = Pick(result.AlbumArtist, candidate.AlbumArtist, options.RuleFor(FieldKey.AlbumArtist));
        result.Genre = Pick(result.Genre, candidate.Genre, options.RuleFor(FieldKey.Genre));
        result.Comment = Pick(result.Comment, candidate.Comment, options.RuleFor(FieldKey.Comment));
        result.Composer = Pick(result.Composer, candidate.Composer, options.RuleFor(FieldKey.Composer));
        result.Lyricist = Pick(result.Lyricist, candidate.Lyricist, options.RuleFor(FieldKey.Lyricist));
        result.Lyrics = Pick(result.Lyrics, candidate.Lyrics, options.RuleFor(FieldKey.Lyrics));

        result.Year = PickNumber(result.Year, candidate.Year, options.RuleFor(FieldKey.Year));
        result.Track = PickNumber(result.Track, candidate.Track, options.RuleFor(FieldKey.Track));
        result.Disc = PickNumber(result.Disc, candidate.Disc, options.RuleFor(FieldKey.Disc));

        MergePictures(result, current, candidate, options.PicturesRule);

        return result;
    }

    /// <summary>
    /// 封面只看 <see cref="OverwriteOptions.PicturesRule"/> 与当前封面数量：
    /// 候选没有封面时一律保持现状；Ignore 不动；KeepExisting/FillEmptyOnly 只在当前无封面时补；
    /// Replace 用候选封面替换。
    /// </summary>
    private static void MergePictures(TrackTags result, TrackTags current, TrackTags candidate, OverwriteRule rule)
    {
        if (candidate.Pictures.Count == 0) return;

        var take = rule switch
        {
            OverwriteRule.Ignore => false,
            OverwriteRule.Replace => true,
            _ => current.Pictures.Count == 0,
        };

        if (take) result.Pictures = [.. candidate.Pictures];
    }

    private static string? Pick(string? current, string? candidate, OverwriteRule rule)
    {
        var next = TrackTags.Normalize(candidate);
        return rule switch
        {
            OverwriteRule.Ignore => current,
            OverwriteRule.Replace => next ?? current,
            OverwriteRule.FillEmptyOnly => string.IsNullOrWhiteSpace(current) ? (next ?? current) : current,
            _ => current ?? next,
        };
    }

    private static uint? PickNumber(uint? current, uint? candidate, OverwriteRule rule)
    {
        if (rule == OverwriteRule.Ignore) return current;
        if (candidate is null) return current;
        return rule switch
        {
            OverwriteRule.Replace => candidate,
            OverwriteRule.FillEmptyOnly => current is null or 0 ? candidate : current,
            _ => current is null or 0 ? candidate : current,
        };
    }
}
