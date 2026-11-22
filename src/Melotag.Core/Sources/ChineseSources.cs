namespace Melotag.Core.Sources;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

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
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        var ids = new List<long>();

        foreach (var song in songs.EnumerateArray())
        {
            if (JsonRead.Int64(song, "id") is not { } id) continue;
            ids.Add(id);

            var artists = song.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                ? string.Join(" / ", a.EnumerateArray()
                    .Select(x => JsonRead.String(x, "name"))
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                : null;
            var album = song.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object
                ? JsonRead.String(al, "name")
                : null;
            var duration = JsonRead.Int64(song, "duration") is { } milliseconds
                ? TimeSpan.FromMilliseconds(milliseconds)
                : (TimeSpan?)null;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonRead.String(song, "name") ?? "",
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
            // 保持候选原顺序：命中歌词的补上歌词，没有歌词的也照常返回。
            for (var i = 0; i < list.Count && i < 5; i++)
            {
                var id = list[i].ReleaseId;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var lyrics = await LoadLyricsAsync(id, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    list[i] = list[i] with { LyricsText = lyrics };
            }
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
            if (!doc.RootElement.TryGetProperty("songs", out var songs) ||
                songs.ValueKind != JsonValueKind.Array) return map;

            foreach (var song in songs.EnumerateArray())
            {
                if (JsonRead.Int64(song, "id") is not { } id) continue;
                if (song.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object &&
                    JsonRead.String(album, "picUrl") is { Length: > 0 } cover)
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
            if (!doc.RootElement.TryGetProperty("lrc", out var lrc) || lrc.ValueKind != JsonValueKind.Object)
                return null;
            return JsonRead.String(lrc, "lyric");
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

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("song", out var song) ||
            song.ValueKind != JsonValueKind.Object ||
            !song.TryGetProperty("itemlist", out var items) ||
            items.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        foreach (var item in items.EnumerateArray().Take(Math.Clamp(limit, 1, 30)))
        {
            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonRead.String(item, "name") ?? "",
                Artist: JsonRead.String(item, "singer"),
                ReleaseId: JsonRead.String(item, "mid")));
        }

        if ((capability & SourceCapability.Lyrics) != 0)
        {
            // 保持候选原顺序：命中歌词的补上歌词，没有歌词的也照常返回。
            for (var i = 0; i < list.Count && i < 5; i++)
            {
                var lyrics = await LoadLyricsAsync(list[i].ReleaseId, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    list[i] = list[i] with { LyricsText = lyrics };
            }
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
            var text = JsonRead.AsString(lyric);
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

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("lists", out var lists) ||
            lists.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        foreach (var item in lists.EnumerateArray())
        {
            var image = JsonRead.String(item, "Image");
            var cover = string.IsNullOrWhiteSpace(image) ? null : image.Replace("{size}", "240");

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonRead.String(item, "SongName") ?? "",
                Artist: JsonRead.String(item, "SingerName"),
                Album: JsonRead.String(item, "AlbumName"),
                CoverUrl: cover,
                ReleaseId: JsonRead.String(item, "FileHash"),
                Detail: JsonRead.Int32(item, "Duration") is { } seconds
                    ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")
                    : null));
        }

        if ((capability & SourceCapability.Lyrics) != 0)
        {
            // 保持候选原顺序：命中歌词的补上歌词，没有歌词的也照常返回。
            for (var i = 0; i < list.Count && i < 3; i++)
            {
                var lyrics = await LoadLyricsAsync(list[i], ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                    list[i] = list[i] with { LyricsText = lyrics };
            }
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
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0) return null;

            var first = candidates[0];
            var id = JsonRead.String(first, "id");
            var key = JsonRead.String(first, "accesskey");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key)) return null;

            var downloadUrl = $"https://krcs.kugou.com/download?ver=1&client=pc&id={Uri.EscapeDataString(id)}&accesskey={Uri.EscapeDataString(key)}&fmt=lrc&charset=utf8";
            var downloadJson = await Http.GetStringAsync(downloadUrl, ct);
            using var downloadDoc = JsonDocument.Parse(downloadJson);
            if (!downloadDoc.RootElement.TryGetProperty("content", out var content)) return null;

            var base64 = JsonRead.AsString(content);
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
    public override string Id => "kuwo";
    public override string DisplayName => "酷我音乐";
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
    {
        var term = query.SearchText(useFileName: false, useArtist: true, useAlbum: false);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var count = Math.Clamp(limit, 1, 30);
        var url = "https://search.kuwo.cn/r.s" +
                  $"?all={Uri.EscapeDataString(term)}&ft=music&itemset=web_2013&client=kt&pn=0&rn={count}&rformat=json&encoding=utf8";
        var raw = await Http.GetStringAsync(url, ct);

        var start = raw.IndexOf('{');
        if (start < 0) return [];

        using var doc = JsonDocument.Parse(NormalizeQuotes(WebUtility.HtmlDecode(raw[start..])));
        if (!doc.RootElement.TryGetProperty("abslist", out var listElement) ||
            listElement.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TagCandidate>();
        foreach (var item in listElement.EnumerateArray())
        {
            var pic = JsonRead.String(item, "web_albumpic_short");
            var cover = string.IsNullOrWhiteSpace(pic) ? null : $"https://img1.kuwo.cn/star/albumcover/{pic}";
            var duration = JsonRead.String(item, "DURATION") is { } durationText
                && int.TryParse(durationText, out var seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;

            list.Add(new TagCandidate(
                SourceId: Id,
                Title: JsonRead.String(item, "SONGNAME") ?? "",
                Artist: JsonRead.String(item, "ARTIST"),
                Album: JsonRead.String(item, "ALBUM"),
                CoverUrl: cover,
                ReleaseId: JsonRead.String(item, "MUSICRID"),
                Detail: duration?.ToString(@"m\:ss")));
        }

        return list;
    }

    /// <summary>
    /// 把该接口的单引号伪 JSON 归一化成标准 JSON。不能简单地把所有 <c>'</c> 换成 <c>"</c>：
    /// 歌名里的撇号（<c>Guns N' Roses</c>、<c>Don't Stop</c>）会被当成引号，导致整段 JSON 失效。
    /// 规则：字符串外遇到 <c>'</c> 开始字符串；字符串内遇到 <c>'</c>，只有当其后（跳过空白）
    /// 是 <c>, : } ]</c> 或输入结束时才视为结束引号，否则保留为 <c>\u0027</c>。
    /// </summary>
    private static string NormalizeQuotes(string raw)
    {
        var output = new StringBuilder(raw.Length + 16);
        var inString = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (!inString)
            {
                if (c == '\'')
                {
                    output.Append('"');
                    inString = true;
                }
                else
                {
                    output.Append(c);
                }

                continue;
            }

            if (c == '\\')
            {
                // 已是转义序列：保留转义语义，只把 \' 改成 JSON 合法的 \u0027。
                if (i + 1 < raw.Length && raw[i + 1] == '\'')
                {
                    output.Append("\\u0027");
                    i++;
                }
                else if (i + 1 < raw.Length)
                {
                    output.Append(c).Append(raw[i + 1]);
                    i++;
                }
                else
                {
                    output.Append("\\\\");
                }

                continue;
            }

            if (c == '\'')
            {
                var next = i + 1;
                while (next < raw.Length && char.IsWhiteSpace(raw[next])) next++;
                var closesString = next >= raw.Length || raw[next] is ',' or ':' or '}' or ']';
                if (closesString)
                {
                    output.Append('"');
                    inString = false;
                }
                else
                {
                    output.Append("\\u0027");
                }

                continue;
            }

            // 单引号字符串里出现的裸双引号（含 HtmlDecode 解出的 &quot;）必须转义。
            if (c == '"')
            {
                output.Append("\\\"");
                continue;
            }

            output.Append(c);
        }

        // 未闭合的字符串补上引号，交给 JsonDocument 正常报错或解析。
        if (inString) output.Append('"');
        return output.ToString();
    }
}

/// <summary>
/// 国内平台接口的宽松读取：字段缺失、类型不符（数字/对象/数组混用）时返回 null，
/// 而不是让 GetString()/GetInt64() 抛异常把整个源变成「无结果」。
/// </summary>
file static class JsonRead
{
    public static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string? AsString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    public static long? Int64(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    public static int? Int32(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
