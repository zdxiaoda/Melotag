namespace Melotag.Core.Sources;

using System.Text.Json;
using Melotag.Core.Abstractions;

/// <summary>
/// AcoustID 音频指纹查询（https://acoustid.org）。应用级 key 内置于
/// <see cref="DefaultApiKey"/>（开源应用可免费申请），无需用户填写；
/// 桌面与 Android 各自注入 <see cref="IFingerprinter"/>，指纹按文件键缓存。
/// </summary>
public sealed class AcoustIdSource : TagSourceBase
{
    /// <summary>应用级 key（https://acoustid.org/api-key 免费申请）。</summary>
    public const string DefaultApiKey = "QstSMM0sT6";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    private const int FingerprintCacheLimit = 512;
    private static readonly Dictionary<string, (long Length, DateTime Mtime, FingerprintResult Fp)>
        FingerprintCache = [];

    private readonly IFingerprinter? _fingerprinter;

    public AcoustIdSource(IFingerprinter? fingerprinter) => _fingerprinter = fingerprinter;

    public override string Id => "acoustid";
    public override string DisplayName => "AcoustID";
    public override SourceCapability Capabilities => SourceCapability.Tags;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit,
        SourceCapability capability, CancellationToken ct)
    {
        LastError = null;
        if (string.IsNullOrWhiteSpace(DefaultApiKey))
            return [];
        if (_fingerprinter is not { } fp)
            return [];
        if (!fp.IsAvailable)
        {
            // 平台解码组件缺失时给出可操作的提示，而不是静默无结果。
            LastError = OperatingSystem.IsAndroid()
                ? "AcoustID 不可用：系统音频解码器初始化失败"
                : "AcoustID 不可用：需要系统解码组件（Linux 安装 gstreamer1.0-libav）";
            return [];
        }

        // 纯文本搜索（无文件句柄）不参与指纹，不算错误。
        if (query.File is not { } file) return [];

        var fingerprint = await GetFingerprintAsync(fp, file, ct);
        if (fingerprint is null)
        {
            LastError = fp.LastError ?? "AcoustID：无法解码该文件";
            return [];
        }

        // Android 端只分析前 120 秒，查询时长优先用容器时长（更接近 AcoustID 记录）。
        var duration = query.Duration is { TotalSeconds: > 1 } d
            ? (int)Math.Round(d.TotalSeconds)
            : fingerprint.DurationSeconds;

        await ThrottleAsync(ct);
        var url = "https://api.acoustid.org/v2/lookup" +
                  $"?client={Uri.EscapeDataString(DefaultApiKey)}" +
                  "&meta=recordings+releases" +
                  $"&duration={duration}" +
                  $"&fingerprint={Uri.EscapeDataString(fingerprint.Fingerprint)}";

        string json;
        try
        {
            json = await Http.GetStringAsync(url, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }

        return Parse(json, Id, limit);
    }

    /// <summary>解析 AcoustID 查询响应：按 result.score 排序取 recording，去重后映射为候选。</summary>
    public static IReadOnlyList<TagCandidate> Parse(string json, string sourceId, int limit)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];

        var candidates = new List<TagCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("recordings", out var recordings)) continue;

            var score = result.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number
                ? scoreEl.GetDouble()
                : 0;

            foreach (var recording in recordings.EnumerateArray())
            {
                if (candidates.Count >= limit) break;
                if (!recording.TryGetProperty("id", out var idEl)
                    || idEl.GetString() is not { Length: > 0 } id) continue;
                if (!seen.Add(id)) continue;

                if (!recording.TryGetProperty("title", out var titleEl)
                    || titleEl.GetString() is not { Length: > 0 } title) continue;

                var artist = recording.TryGetProperty("artists", out var artists)
                    ? string.Join(" & ", artists.EnumerateArray()
                        .Where(a => a.TryGetProperty("name", out var name))
                        .Select(a => a.GetProperty("name").GetString()))
                    : null;

                string? album = null;
                uint? year = null;
                if (recording.TryGetProperty("releases", out var releases)
                    && releases.GetArrayLength() > 0)
                {
                    var release = releases[0];
                    if (release.TryGetProperty("title", out var albumEl))
                        album = albumEl.GetString();
                    if (release.TryGetProperty("date", out var dateEl) && dateEl.GetString() is { Length: >= 4 } date
                        && uint.TryParse(date[..4], out var parsedYear))
                        year = parsedYear;
                }

                candidates.Add(new TagCandidate(
                    SourceId: sourceId,
                    Title: title,
                    Artist: string.IsNullOrWhiteSpace(artist) ? null : artist,
                    Album: album,
                    Year: year,
                    ReleaseId: id,
                    Detail: $"{(int)Math.Round(score * 100)}%"));
            }

            if (candidates.Count >= limit) break;
        }

        return candidates;
    }

    private static async Task<FingerprintResult?> GetFingerprintAsync(IFingerprinter fp, IFileHandle file,
        CancellationToken ct)
    {
        var mtime = file.LastWriteTimeUtc;
        if (FingerprintCache.TryGetValue(file.Key, out var hit)
            && hit.Length == file.Length && hit.Mtime == mtime)
            return hit.Fp;

        var result = await fp.ComputeAsync(file, ct);
        if (result is null) return null;

        if (FingerprintCache.Count >= FingerprintCacheLimit)
            FingerprintCache.Clear();
        FingerprintCache[file.Key] = (file.Length, mtime, result);

        return result;
    }

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed < TimeSpan.FromMilliseconds(350))
                await Task.Delay(TimeSpan.FromMilliseconds(350) - elapsed, ct);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            Gate.Release();
        }
    }
}
