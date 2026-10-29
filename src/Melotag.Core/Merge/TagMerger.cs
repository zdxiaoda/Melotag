namespace Melotag.Core.Merge;

using Melotag.Core.Models;

public enum OverwriteRule
{
    KeepExisting,
    FillEmptyOnly,
    Replace,
}

public sealed class OverwriteOptions
{
    public OverwriteRule Default { get; set; } = OverwriteRule.FillEmptyOnly;
    public Dictionary<FieldKey, OverwriteRule> PerField { get; } = [];

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

        if (candidate.Pictures.Count > 0 &&
            options.RuleFor(FieldKey.Title) != OverwriteRule.KeepExisting &&
            (current.Pictures.Count == 0 || options.Default == OverwriteRule.Replace))
        {
            result.Pictures = [.. candidate.Pictures];
        }

        return result;
    }

    private static string? Pick(string? current, string? candidate, OverwriteRule rule)
    {
        var next = TrackTags.Normalize(candidate);
        return rule switch
        {
            OverwriteRule.Replace => next ?? current,
            OverwriteRule.FillEmptyOnly => string.IsNullOrWhiteSpace(current) ? (next ?? current) : current,
            _ => current ?? next,
        };
    }

    private static uint? PickNumber(uint? current, uint? candidate, OverwriteRule rule)
    {
        if (candidate is null) return current;
        return rule switch
        {
            OverwriteRule.Replace => candidate,
            OverwriteRule.FillEmptyOnly => current is null or 0 ? candidate : current,
            _ => current is null or 0 ? candidate : current,
        };
    }
}
