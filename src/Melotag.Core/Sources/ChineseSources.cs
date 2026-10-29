namespace Melotag.Core.Sources;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// 国内平台适配器。均为公开 Web 接口的只读查询，不内置任何版权内容；
/// 歌词/封面在用户请求时按需下载。接口可能随平台调整而失效，失败时返回空结果。
/// </summary>
public sealed class NeteaseSource : TagSourceBase
{
    private const string Referer = "https://music.163.com";

    public override string Id => "netease";
    public override string DisplayName => "网易云音乐";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover | SourceCapability.Lyrics;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: true);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var count = Math.Clamp(limit, 1, 30);
        var url = $"https://music.163.com/api/search/get/web?csrf_token=&s={Uri.EscapeDataString(term)}&type=1&offset=0&total=true&limit={count}";
        var json = await Http.GetStringAsync(url, ct, Referer);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs)) return [];

        var list = new List<TagCandidate>();
        var ids = new List<long>();

        foreach (var song in songs.EnumerateArray())
        {
            var id = song.GetProperty("id").GetInt64();
            ids.Add(id);

            var artists = song.TryGetProperty("artists", out var a)
                ? string.Join(" / ", a.EnumerateArray().Select(x => x.GetProperty("name").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : null;
            var album = song.TryGetProperty("album", out var al) && al.TryGetProperty("name", out var an) ? an.GetString() : null;
            var duration = song.TryGetProperty("duration", out var d) ? TimeSpan.FromMilliseconds(d.GetInt64()) : (TimeSpan?)null;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: song.GetProperty("name").GetString() ?? "",
                Artist: artists,
                Album: album,
                Detail: duration?.ToString(@"m\:ss"),
                ReleaseId: id.ToString(CultureInfo.InvariantCulture)));
        }

        if ((capability & SourceCapability.Cover) != 0)
        {
            var covers = await LoadCoversAsync(ids, ct);
            for (var i = 0; i < list.Count; i++)
                if (covers.TryGetValue(ids[i], out var cover))
                    list[i] = list[i] with { CoverUrl = cover };
        }

        if ((capability & SourceCapability.Lyrics) != 0)
        {
            var withLyrics = new List<TagCandidate>();
            foreach (var candidate in list.Take(Math.Min(list.Count, 5)))
            {
                var lyrics = await LoadLyricsAsync(candidate.ReleaseId!, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    withLyrics.Add(candidate with { LyricsText = lyrics });
            }

            if (withLyrics.Count > 0) return withLyrics;
        }

        return list;
    }

    private static async Task<Dictionary<long, string>> LoadCoversAsync(List<long> ids, CancellationToken ct)
    {
        var map = new Dictionary<long, string>();
        if (ids.Count == 0) return map;

        try
        {
            var idList = string.Join(",", ids);
            var url = $"https://music.163.com/api/song/detail/?id={ids[0]}&ids=%5B{Uri.EscapeDataString(idList)}%5D";
            var json = await Http.GetStringAsync(url, ct, Referer);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("songs", out var songs)) return map;

            foreach (var song in songs.EnumerateArray())
            {
                var id = song.GetProperty("id").GetInt64();
                if (song.TryGetProperty("album", out var album) && album.TryGetProperty("picUrl", out var pic) &&
                    pic.GetString() is { Length: > 0 } cover)
                    map[id] = cover;
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // 封面缺失不影响标签结果
        }

        return map;
    }

    private static async Task<string?> LoadLyricsAsync(string id, CancellationToken ct)
    {
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={Uri.EscapeDataString(id)}&lv=1&kv=1&tv=-1";
            var json = await Http.GetStringAsync(url, ct, Referer);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("lrc", out var lrc)) return null;
            return lrc.TryGetProperty("lyric", out var text) ? text.GetString() : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>QQ 音乐：smartbox 搜索（歌名/歌手）+ 歌词接口。</summary>
public sealed class QQMusicSource : TagSourceBase
{
    private const string Referer = "https://y.qq.com/portal/player.html";

    public override string Id => "qq";
    public override string DisplayName => "QQ 音乐";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Lyrics;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: false, useAlbum: false);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var url = "https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg" +
                  $"?key={Uri.EscapeDataString(term)}&format=json&g_tk=5381&inCharset=utf-8&outCharset=utf-8" +
                  "&notice=0&platform=yqq.json&needNewCode=0";
        var json = await Http.GetStringAsync(url, ct, "https://y.qq.com/");
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("song", out var song) ||
            !song.TryGetProperty("itemlist", out var items)) return [];

        var list = new List<TagCandidate>();
        foreach (var item in items.EnumerateArray().Take(Math.Clamp(limit, 1, 30)))
        {
            list.Add(new TagCandidate(
                SourceId: Id,
                Title: item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Artist: item.TryGetProperty("singer", out var singer) ? singer.GetString() : null,
                ReleaseId: item.TryGetProperty("mid", out var mid) ? mid.GetString() : null));
        }

        if ((capability & SourceCapability.Lyrics) != 0)
        {
            var withLyrics = new List<TagCandidate>();
            foreach (var candidate in list.Take(Math.Min(list.Count, 5)))
            {
                var lyrics = await LoadLyricsAsync(candidate.ReleaseId, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    withLyrics.Add(candidate with { LyricsText = lyrics });
            }

            if (withLyrics.Count > 0) return withLyrics;
        }

        return list;
    }

    private static async Task<string?> LoadLyricsAsync(string? mid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mid)) return null;

        try
        {
            var url = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={Uri.EscapeDataString(mid)}&format=json&nobase64=1&g_tk=5381";
            var json = await Http.GetStringAsync(url, ct, Referer);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("lyric", out var lyric)) return null;
            var text = lyric.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : WebUtility.HtmlDecode(text);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>酷狗：搜索 + 封面 + KRC 歌词链。</summary>
public sealed class KugouSource : TagSourceBase
{
    public override string Id => "kugou";
    public override string DisplayName => "酷狗音乐";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover | SourceCapability.Lyrics;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: false);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var count = Math.Clamp(limit, 1, 30);
        var url = "https://songsearch.kugou.com/song_search_v2" +
                  $"?keyword={Uri.EscapeDataString(term)}&page=1&pagesize={count}" +
                  "&platform=WebFilter&userid=0&clientver=2000&iscorrection=1&privilege_filter=0&filter=10&appid=1014";
        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("lists", out var lists)) return [];

        var list = new List<TagCandidate>();
        foreach (var item in lists.EnumerateArray())
        {
            var image = item.TryGetProperty("Image", out var img) ? img.GetString() : null;
            var cover = string.IsNullOrWhiteSpace(image) ? null : image!.Replace("{size}", "240");

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: item.TryGetProperty("SongName", out var sn) ? sn.GetString() ?? "" : "",
                Artist: item.TryGetProperty("SingerName", out var si) ? si.GetString() : null,
                Album: item.TryGetProperty("AlbumName", out var an) ? an.GetString() : null,
                CoverUrl: cover,
                ReleaseId: item.TryGetProperty("FileHash", out var fh) ? fh.GetString() : null,
                Detail: item.TryGetProperty("Duration", out var du) ? TimeSpan.FromSeconds(du.GetInt32()).ToString(@"m\:ss") : null));
        }

        if ((capability & SourceCapability.Lyrics) != 0)
        {
            var withLyrics = new List<TagCandidate>();
            foreach (var candidate in list.Take(Math.Min(list.Count, 3)))
            {
                var lyrics = await LoadLyricsAsync(candidate, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    withLyrics.Add(candidate with { LyricsText = lyrics });
            }

            if (withLyrics.Count > 0) return withLyrics;
        }

        return list;
    }

    private static async Task<string?> LoadLyricsAsync(TagCandidate candidate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidate.ReleaseId)) return null;

        try
        {
            var keyword = $"{candidate.Title} {candidate.Artist}".Trim();
            var searchUrl = "https://krcs.kugou.com/search?ver=1&man=yes&client=mobi" +
                            $"&keyword={Uri.EscapeDataString(keyword)}&duration=&hash={Uri.EscapeDataString(candidate.ReleaseId)}";
            var searchJson = await Http.GetStringAsync(searchUrl, ct);
            using var searchDoc = JsonDocument.Parse(searchJson);
            if (!searchDoc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0) return null;

            var first = candidates[0];
            var id = first.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var key = first.TryGetProperty("accesskey", out var keyElement) ? keyElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key)) return null;

            var downloadUrl = $"https://krcs.kugou.com/download?ver=1&client=pc&id={Uri.EscapeDataString(id!)}&accesskey={Uri.EscapeDataString(key!)}&fmt=lrc&charset=utf8";
            var downloadJson = await Http.GetStringAsync(downloadUrl, ct);
            using var downloadDoc = JsonDocument.Parse(downloadJson);
            if (!downloadDoc.RootElement.TryGetProperty("content", out var content)) return null;

            var base64 = content.GetString();
            if (string.IsNullOrWhiteSpace(base64)) return null;
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>酷我：搜索与封面。歌词接口目前返回 301，暂不提供歌词。</summary>
public sealed class KuwoSource : TagSourceBase
{
    private static readonly Regex JsonQuotes = new("'", RegexOptions.Compiled);

    public override string Id => "kuwo";
    public override string DisplayName => "酷我音乐";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: false);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var count = Math.Clamp(limit, 1, 30);
        var url = "http://search.kuwo.cn/r.s" +
                  $"?all={Uri.EscapeDataString(term)}&ft=music&itemset=web_2013&client=kt&pn=0&rn={count}&rformat=json&encoding=utf8";
        var raw = await Http.GetStringAsync(url, ct);

        // 该接口返回单引号伪 JSON
        var start = raw.IndexOf('{');
        if (start < 0) return [];
        var normalized = WebUtility.HtmlDecode(JsonQuotes.Replace(raw[start..], "\""));

        using var doc = JsonDocument.Parse(normalized);
        if (!doc.RootElement.TryGetProperty("abslist", out var listElement)) return [];

        var list = new List<TagCandidate>();
        foreach (var item in listElement.EnumerateArray())
        {
            var pic = item.TryGetProperty("web_albumpic_short", out var p) ? p.GetString() : null;
            var cover = string.IsNullOrWhiteSpace(pic) ? null : $"https://img1.kuwo.cn/star/albumcover/{pic}";
            var artist = item.TryGetProperty("ARTIST", out var a) ? a.GetString() : null;
            var album = item.TryGetProperty("ALBUM", out var al) ? al.GetString() : null;
            var duration = item.TryGetProperty("DURATION", out var d) && int.TryParse(d.GetString(), out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: item.TryGetProperty("SONGNAME", out var sn) ? sn.GetString() ?? "" : "",
                Artist: artist,
                Album: album,
                CoverUrl: cover,
                ReleaseId: item.TryGetProperty("MUSICRID", out var rid) ? rid.GetString() : null,
                Detail: duration?.ToString(@"m\:ss")));
        }

        return list;
    }
}
