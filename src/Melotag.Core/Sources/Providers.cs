namespace Melotag.Core.Sources;

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
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("releases", out var releases) ||
            releases.ValueKind != JsonValueKind.Array) return [];

        var results = new List<TagCandidate>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;

            var id = JsonFields.String(release, "id") ?? "";
            var title = JsonFields.String(release, "title") ?? "";

            var artist = "";
            if (release.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                artist = string.Join(" & ", credits.EnumerateArray()
                    .Select(c => JsonFields.String(c, "name"))
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            uint? year = null;
            if (JsonFields.String(release, "date") is { Length: >= 4 } d && uint.TryParse(d[..4], out var y)) year = y;

            uint? trackCount = null;
            if (release.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array
                && media.GetArrayLength() > 0
                && JsonFields.Int32(media[0], "track-count") is { } count && count > 0)
                trackCount = (uint)count;

            var type = release.TryGetProperty("release-group", out var rg)
                        && rg.ValueKind == JsonValueKind.Object
                ? JsonFields.String(rg, "primary-type")
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

        var configured = _country();
        var country = string.IsNullOrWhiteSpace(configured) ? "US" : configured;
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
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        foreach (var item in results.EnumerateArray())
        {
            uint? year = null;
            if (JsonFields.String(item, "releaseDate") is { Length: >= 4 } d && uint.TryParse(d[..4], out var y)) year = y;

            var artwork = JsonFields.String(item, "artworkUrl100");
            var cover = artwork?.Replace("100x100bb", "1200x1200bb");

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonFields.String(item, "trackName") ?? "",
                Artist: JsonFields.String(item, "artistName"),
                Album: JsonFields.String(item, "collectionName"),
                Year: year,
                Track: JsonFields.UInt32(item, "trackNumber"),
                Disc: JsonFields.UInt32(item, "discNumber"),
                Genre: JsonFields.String(item, "primaryGenreName"),
                CoverUrl: cover,
                Detail: JsonFields.Int64(item, "trackTimeMillis") is { } ms
                    ? TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss")
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

    /// <summary>没有配置 API key 时不参与搜索，也不产生“请填写 key”之类的提示。</summary>
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey());

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        LastError = null;
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

        // Last.fm 出错时（key 无效、限流等）返回 error/message，而不是 results。
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("error", out _))
        {
            var message = JsonFields.String(doc.RootElement, "message");
            LastError = string.IsNullOrWhiteSpace(message)
                ? "Last.fm：接口返回错误"
                : $"Last.fm：{message}";
            return [];
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Object ||
            !results.TryGetProperty("trackmatches", out var matches) ||
            matches.ValueKind != JsonValueKind.Object ||
            !matches.TryGetProperty("track", out var tracks) ||
            tracks.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<TagCandidate>();
        foreach (var t in tracks.EnumerateArray())
        {
            var artist = JsonFields.String(t, "artist");
            var title = JsonFields.String(t, "name") ?? "";

            string? cover = null;
            if (t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("image", out var images)
                && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (JsonFields.String(img, "size") == "extralarge")
                        cover = JsonFields.String(img, "#text");
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
            var synced = JsonFields.String(item, "syncedLyrics");
            var plain = JsonFields.String(item, "plainLyrics");
            var lyrics = string.IsNullOrWhiteSpace(synced) ? plain : synced;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonFields.String(item, "trackName") ?? "",
                Artist: JsonFields.String(item, "artistName"),
                Album: JsonFields.String(item, "albumName"),
                LyricsText: lyrics,
                Detail: string.IsNullOrWhiteSpace(synced) ? "纯文本" : "同步歌词"));
        }

        return list;
    }
}

/// <summary>
/// 在线接口的宽松读取：字段缺失或类型不符时返回 null，避免 GetString()/GetInt32()
/// 因为一次结构变化把整源拖成异常。
/// </summary>
file static class JsonFields
{
    public static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Int32(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    public static long? Int64(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    /// <summary>读取非负整数；负数或缺失返回 null（负数直接转 uint 会得到天文数字）。</summary>
    public static uint? UInt32(JsonElement element, string property) =>
        Int32(element, property) is { } number && number >= 0 ? (uint)number : null;
}
