// 网络源诊断探针：不依赖 UI，直接验证适配器的网络与解析路径。
using Melotag.Core.Sources;

var sources = new ITagSource[]
{
    new NeteaseSource(),
    new QQMusicSource(),
    new KugouSource(),
    new KuwoSource(),
    new MusicBrainzSource(),
    new CoverArtArchiveSource(),
    new ITunesSource(() => "US"),
    new LastFmSource(() => Environment.GetEnvironmentVariable("LASTFM_API_KEY")),
    new LrclibSource(),
};

var search = new TagSearchService(sources);

var title = args.Length > 0 ? args[0] : "Get Lucky";
var artist = args.Length > 1 ? args[1] : "Daft Punk";
var query = new TagQuery { Title = title, Artist = artist };

Console.WriteLine($"Query: {title} / {artist}");
Console.WriteLine("--- tags ---");
try
{
    var tags = await search.SearchAsync(query, SourceCapability.Tags,
        ["netease", "qq", "kugou", "kuwo", "musicbrainz", "itunes"], 3, 3, CancellationToken.None);
    Console.WriteLine($"count={tags.Count}");
    foreach (var t in tags.Take(8))
        Console.WriteLine($"  [{t.SourceId}] {t.Title} | {t.Artist} | {t.Album} | {t.Year} | cover={(t.CoverUrl is not null)}");
}
catch (Exception ex)
{
    Console.WriteLine("tags failed: " + ex.Message);
}

Console.WriteLine("--- cover ---");
try
{
    var covers = await search.SearchAsync(query, SourceCapability.Cover,
        ["netease", "kugou", "kuwo", "itunes", "coverart"], 2, 2, CancellationToken.None);
    Console.WriteLine($"count={covers.Count}");
    var first = covers.FirstOrDefault(c => c.CoverUrl is not null);
    if (first is not null)
    {
        var bytes = await search.Find(first.SourceId)!.DownloadCoverAsync(first.CoverUrl!, CancellationToken.None);
        Console.WriteLine($"  downloaded {bytes?.Length ?? 0} bytes from {first.SourceId}");
    }
}
catch (Exception ex)
{
    Console.WriteLine("cover failed: " + ex.Message);
}

Console.WriteLine("--- lyrics ---");
try
{
    var lyrics = await search.SearchAsync(query, SourceCapability.Lyrics,
        ["netease", "qq", "kugou", "lrclib"], 3, 1, CancellationToken.None);
    Console.WriteLine($"count={lyrics.Count}");
    foreach (var l in lyrics.Take(3))
        Console.WriteLine($"  [{l.SourceId}] {l.Title} | {l.Artist} | chars={l.LyricsText?.Length ?? 0}");
}
catch (Exception ex)
{
    Console.WriteLine("lyrics failed: " + ex.Message);
}
