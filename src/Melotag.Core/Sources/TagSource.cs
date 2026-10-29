namespace Melotag.Core.Sources;

using Melotag.Core.Models;

public sealed record TagQuery
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? FileName { get; init; }
    public TimeSpan? Duration { get; init; }

    public static TagQuery From(TrackItem item) => new()
    {
        Title = item.Tags.Title,
        Artist = item.Tags.Artist,
        Album = item.Tags.Album,
        FileName = Path.GetFileNameWithoutExtension(item.Path),
        Duration = item.Info?.Duration,
    };

    public string SearchText(bool useFileName, bool useArtist, bool useAlbum)
    {
        var parts = new List<string>();
        if (useFileName && !string.IsNullOrWhiteSpace(FileName)) parts.Add(FileName!);
        else if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title!);
        if (useArtist && !string.IsNullOrWhiteSpace(Artist)) parts.Add(Artist!);
        if (useAlbum && !string.IsNullOrWhiteSpace(Album)) parts.Add(Album!);
        return string.Join(' ', parts.Where(p => p.Length > 0));
    }
}

[Flags]
public enum SourceCapability
{
    None = 0,
    Tags = 1,
    Cover = 2,
    Lyrics = 4,
}

public sealed record TagCandidate(
    string SourceId,
    string Title,
    string? Artist = null,
    string? Album = null,
    string? AlbumArtist = null,
    uint? Year = null,
    uint? Track = null,
    uint? Disc = null,
    string? Genre = null,
    string? CoverUrl = null,
    string? ReleaseId = null,
    string? LyricsText = null,
    string? Detail = null)
{
    public string Summary =>
        string.Join(" · ", new[] { Artist, Album, Year?.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public TrackTags ToTags() => new()
    {
        Title = TrackTags.Normalize(Title),
        Artist = TrackTags.Normalize(Artist),
        Album = TrackTags.Normalize(Album),
        AlbumArtist = TrackTags.Normalize(AlbumArtist),
        Year = Year,
        Track = Track,
        Disc = Disc,
        Genre = TrackTags.Normalize(Genre),
        Lyrics = TrackTags.Normalize(LyricsText),
    };
}

public interface ITagSource
{
    string Id { get; }
    string DisplayName { get; }
    SourceCapability Capabilities { get; }
    Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct);
    Task<byte[]?> DownloadCoverAsync(string coverUrl, CancellationToken ct);
    Task<string?> DownloadLyricsAsync(TagCandidate candidate, CancellationToken ct);
}

public abstract class TagSourceBase : ITagSource
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract SourceCapability Capabilities { get; }

    public virtual Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TagCandidate>>([]);

    public virtual Task<byte[]?> DownloadCoverAsync(string coverUrl, CancellationToken ct) =>
        Http.TryGetBytesAsync(coverUrl, ct);

    public virtual Task<string?> DownloadLyricsAsync(TagCandidate candidate, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}

public static class Http
{
    private static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        return client;
    }

    public static string UserAgent { get; set; } = "MelotagClone/0.2 (https://github.com/example/musictag-clone)";

    public static async Task<string> GetStringAsync(string url, CancellationToken ct, string? referer = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (referer is not null) request.Headers.TryAddWithoutValidation("Referer", referer);
            using var response = await Client.SendAsync(request, ct);

            if ((int)response.StatusCode is 429 or 503 && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5 * (attempt + 1));
                await Task.Delay(delay, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
    }

    public static async Task<byte[]?> TryGetBytesAsync(string url, CancellationToken ct, string? referer = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (referer is not null) request.Headers.TryAddWithoutValidation("Referer", referer);
            using var response = await Client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
