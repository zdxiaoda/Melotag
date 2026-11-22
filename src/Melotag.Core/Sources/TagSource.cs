namespace Melotag.Core.Sources;

using System.IO.Compression;
using System.Net.Http.Headers;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;

public sealed record TagQuery
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? FileName { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>指纹源（AcoustID）用；纯文本搜索时不提供。</summary>
    public IFileHandle? File { get; init; }

    public static TagQuery From(TrackItem item) => new()
    {
        Title = item.Tags.Title,
        Artist = item.Tags.Artist,
        Album = item.Tags.Album,
        FileName = Path.GetFileNameWithoutExtension(item.Path),
        Duration = item.Info?.Duration,
        File = item.Source,
    };

    /// <summary>
    /// 组合搜索词。标题为空时回落到文件名，让未打标签的文件也能查；搜索词完全为空时返回空串，
    /// 调用方据此跳过该源。
    /// </summary>
    public string SearchText(bool useFileName, bool useArtist, bool useAlbum)
    {
        var primary = useFileName
            ? (string.IsNullOrWhiteSpace(FileName) ? Title : FileName)
            : (string.IsNullOrWhiteSpace(Title) ? FileName : Title);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary)) parts.Add(primary.Trim());
        if (useArtist && !string.IsNullOrWhiteSpace(Artist)) parts.Add(Artist.Trim());
        if (useAlbum && !string.IsNullOrWhiteSpace(Album)) parts.Add(Album.Trim());
        return string.Join(' ', parts);
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

    /// <summary>当前配置下是否可以实际查询；未配置必需凭据的源不参与搜索。</summary>
    bool IsConfigured { get; }

    /// <summary>最近一次搜索的用户可见错误（如缺少解码器）；无错误为 null。</summary>
    string? LastError { get; }

    Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct);
    Task<byte[]?> DownloadCoverAsync(string coverUrl, CancellationToken ct);
}

public abstract class TagSourceBase : ITagSource
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract SourceCapability Capabilities { get; }

    public virtual bool IsConfigured => true;

    /// <summary>子类在搜索失败时写入用户可处理的提示。</summary>
    public string? LastError { get; protected set; }

    /// <summary>每次搜索前清掉上一次的错误，避免“上次失败、这次成功”仍显示旧提示。</summary>
    internal void ResetError() => LastError = null;

    /// <summary>供搜索调度器在源抛出异常时写入简短提示，避免失败被吞成「无结果」。</summary>
    internal void SetError(string message) =>
        LastError = string.IsNullOrWhiteSpace(message) ? null : message;

    public virtual Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit, SourceCapability capability, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TagCandidate>>([]);

    public virtual Task<byte[]?> DownloadCoverAsync(string coverUrl, CancellationToken ct) =>
        Http.TryGetBytesAsync(coverUrl, ct);
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

    public static string UserAgent { get; set; } = ProductInfo.UserAgent;

    public static async Task<string> GetStringAsync(string url, CancellationToken ct, string? referer = null, string? accept = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (referer is not null) request.Headers.TryAddWithoutValidation("Referer", referer);
            if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
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

    /// <summary>
    /// POST 表单查询。指纹等参数可能长达数千字符，走 GET 容易被网关以 414 拒绝；
    /// AcoustID 官方推荐压缩 POST，因此请求体用 gzip（<c>Content-Encoding: gzip</c>）。
    /// 4xx/5xx 也返回响应体：AcoustID 对无效指纹等业务错误返回 HTTP 400 + JSON
    /// <c>status:"error"</c>，调用方需要读取 body 才能给出具体原因。
    /// 重试/退避策略与 <see cref="GetStringAsync"/> 保持一致。
    /// </summary>
    public static async Task<(int Status, string Body)> PostFormWithStatusAsync(
        string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        byte[] body;
        using (var formContent = new FormUrlEncodedContent(form))
            body = await formContent.ReadAsByteArrayAsync(ct);

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            await gzip.WriteAsync(body, ct);
        var payload = compressed.ToArray();

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(payload),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            request.Content.Headers.ContentEncoding.Add("gzip");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await Client.SendAsync(request, ct);

            if ((int)response.StatusCode is 429 or 503 && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5 * (attempt + 1));
                await Task.Delay(delay, ct);
                continue;
            }

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
    }

    private const int MaxImageBytes = 32 * 1024 * 1024;

    public static async Task<byte[]?> TryGetBytesAsync(string url, CancellationToken ct, string? referer = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (referer is not null) request.Headers.TryAddWithoutValidation("Referer", referer);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > MaxImageBytes) return null;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxImageBytes) return null;
                output.Write(buffer, 0, read);
            }

            return output.Length == 0 ? null : output.ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
