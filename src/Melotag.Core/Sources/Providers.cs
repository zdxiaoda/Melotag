namespace Melotag.Core.Sources;

using System.Globalization;
using System.Text.Json;

/// <summary>MusicBrainz 官方 API（要求 UA 带联系方式，且 ≤1 req/s）。</summary>
public sealed class MusicBrainzSource : TagSourceBase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    public override string Id => "musicbrainz";
    public override string DisplayName => "MusicBrainz";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: true);
        if (string.IsNullOrWhiteSpace(term)) return [];

        await ThrottleAsync(ct);
        var url = $"https://musicbrainz.org/ws/2/release?query={Uri.EscapeDataString(term)}&fmt=json&limit={Math.Clamp(limit, 1, 25)}";
        var json = await Http.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("releases", out var releases)) return [];

        var results = new List<TagCandidate>();
        foreach (var release in releases.EnumerateArray())
        {
            var id = release.GetProperty("id").GetString() ?? "";
            var title = release.GetProperty("title").GetString() ?? "";

            var artist = "";
            if (release.TryGetProperty("artist-credit", out var credits))
            {
                artist = string.Join(" & ", credits.EnumerateArray()
                    .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            uint? year = null;
            if (release.TryGetProperty("date", out var date) && date.GetString() is { Length: >= 4 } d
                && uint.TryParse(d[..4], out var y)) year = y;

            uint? trackCount = null;
            if (release.TryGetProperty("media", out var media) && media.GetArrayLength() > 0
                && media[0].TryGetProperty("track-count", out var tc)) trackCount = (uint)tc.GetInt32();

            var type = release.TryGetProperty("release-group", out var rg) && rg.TryGetProperty("primary-type", out var pt)
                ? pt.GetString()
                : null;

            results.Add(new TagCandidate(
                SourceId: Id,
                Title: title,
                Artist: artist,
                Album: title,
                Year: year,
                Genre: type,
                CoverUrl: $"https://coverartarchive.org/release/{id}/front-500",
                ReleaseId: id,
                Detail: string.Join(" · ", new[] { type, trackCount is null ? null : $"{trackCount} tracks" }.Where(s => s is not null))));
        }

        return results;
    }

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed < TimeSpan.FromMilliseconds(1100))
                await Task.Delay(TimeSpan.FromMilliseconds(1100) - elapsed, ct);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>Cover Art Archive：按 release id 取正面封面。</summary>
public sealed class CoverArtArchiveSource : TagSourceBase
{
    public override string Id => "coverart";
    public override string DisplayName => "Cover Art Archive";
    public override SourceCapability Capabilities => SourceCapability.Cover;

    public override Task<byte[]?> DownloadCoverAsync(string coverUrl, CancellationToken ct) =>
        Http.TryGetBytesAsync(coverUrl, ct);
}

/// <summary>iTunes Search API（无需 key，按国家取值）。</summary>
public sealed class ITunesSource : TagSourceBase
{
    private readonly Func<string?> _country;

    public ITunesSource(Func<string?> country) => _country = country;

    public override string Id => "itunes";
    public override string DisplayName => "iTunes";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: false);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var country = string.IsNullOrWhiteSpace(_country()) ? "US" : _country()!;
        var list = await SearchCountryAsync(term, limit, country, ct);
        if (list.Count == 0 && !country.Equals("US", StringComparison.OrdinalIgnoreCase))
            list = await SearchCountryAsync(term, limit, "US", ct);

        return list;
    }

    private async Task<List<TagCandidate>> SearchCountryAsync(string term, int limit, string country, CancellationToken ct)
    {
        var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity=song&limit={Math.Clamp(limit, 1, 50)}&country={Uri.EscapeDataString(country)}";
        var json = await Http.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];

        var list = new List<TagCandidate>();
        foreach (var item in results.EnumerateArray())
        {
            uint? year = null;
            if (item.TryGetProperty("releaseDate", out var rd) && rd.GetString() is { Length: >= 4 } d
                && uint.TryParse(d[..4], out var y)) year = y;

            var artwork = item.TryGetProperty("artworkUrl100", out var au) ? au.GetString() : null;
            var cover = artwork?.Replace("100x100bb", "1200x1200bb");

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: item.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                Artist: item.TryGetProperty("artistName", out var an) ? an.GetString() : null,
                Album: item.TryGetProperty("collectionName", out var cn) ? cn.GetString() : null,
                Year: year,
                Track: item.TryGetProperty("trackNumber", out var trn) ? (uint)trn.GetInt32() : null,
                Disc: item.TryGetProperty("discNumber", out var dn) ? (uint)dn.GetInt32() : null,
                Genre: item.TryGetProperty("primaryGenreName", out var gn) ? gn.GetString() : null,
                CoverUrl: cover,
                Detail: item.TryGetProperty("trackTimeMillis", out var ms)
                    ? TimeSpan.FromMilliseconds(ms.GetInt64()).ToString(@"m\:ss")
                    : null));
        }

        return list;
    }
}

/// <summary>Last.fm：需要用户自备 API key，未配置时返回空。</summary>
public sealed class LastFmSource : TagSourceBase
{
    private readonly Func<string?> _apiKey;

    public LastFmSource(Func<string?> apiKey) => _apiKey = apiKey;

    public override string Id => "lastfm";
    public override string DisplayName => "Last.fm";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var key = _apiKey();
        if (string.IsNullOrWhiteSpace(key)) return [];

        var track = query.Title ?? query.FileName;
        if (string.IsNullOrWhiteSpace(track)) return [];

        var url = "https://ws.audioscrobbler.com/2.0/" +
                  $"?method=track.search&track={Uri.EscapeDataString(track!)}" +
                  (string.IsNullOrWhiteSpace(query.Artist) ? "" : $"&artist={Uri.EscapeDataString(query.Artist!)}") +
                  $"&api_key={Uri.EscapeDataString(key!)}&format=json&limit={Math.Clamp(limit, 1, 30)}";

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("trackmatches", out var matches) ||
            !matches.TryGetProperty("track", out var tracks))
            return [];

        var list = new List<TagCandidate>();
        foreach (var t in tracks.EnumerateArray())
        {
            var artist = t.TryGetProperty("artist", out var a) ? a.GetString() : null;
            var title = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            string? cover = null;
            if (t.TryGetProperty("image", out var images))
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (img.TryGetProperty("size", out var size) && size.GetString() == "extralarge")
                        cover = img.TryGetProperty("#text", out var text) ? text.GetString() : null;
                }
            }

            list.Add(new TagCandidate(Id, title, artist, CoverUrl: cover));
        }

        return list;
    }
}

/// <summary>LRCLIB：公开歌词 API，返回原文/同步歌词，无需 key。</summary>
public sealed class LrclibSource : TagSourceBase
{
    public override string Id => "lrclib";
    public override string DisplayName => "LRCLIB";
    public override SourceCapability Capabilities => SourceCapability.Lyrics;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var title = query.Title ?? query.FileName;
        if (string.IsNullOrWhiteSpace(title)) return [];

        var url = "https://lrclib.net/api/search" +
                  $"?track_name={Uri.EscapeDataString(title!)}" +
                  (string.IsNullOrWhiteSpace(query.Artist) ? "" : $"&artist_name={Uri.EscapeDataString(query.Artist!)}") +
                  (string.IsNullOrWhiteSpace(query.Album) ? "" : $"&album_name={Uri.EscapeDataString(query.Album!)}");

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        foreach (var item in doc.RootElement.EnumerateArray().Take(Math.Max(1, limit)))
        {
            var synced = item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var plain = item.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var lyrics = string.IsNullOrWhiteSpace(synced) ? plain : synced;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: item.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                Artist: item.TryGetProperty("artistName", out var an) ? an.GetString() : null,
                Album: item.TryGetProperty("albumName", out var al) ? al.GetString() : null,
                LyricsText: lyrics,
                Detail: string.IsNullOrWhiteSpace(synced) ? "纯文本" : "同步歌词"));
        }

        return list;
    }
}
