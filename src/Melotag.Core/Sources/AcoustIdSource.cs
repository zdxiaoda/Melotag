namespace Melotag.Core.Sources;

using System.Collections.Concurrent;
using System.Globalization;
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

    /// <summary>
    /// 与文件时长相差多少秒以内视为同等匹配。取「3 秒」与「目标时长的 2%」中的较大者，
    /// 并封顶 15 秒：长曲目的版本差异天然更大，但 5% 对三五分钟的歌过宽，
    /// 会把相差 5 秒的不同曲目当成同曲。
    /// </summary>
    private const double MinDurationToleranceSeconds = 3;
    private const double DurationToleranceRatio = 0.02;
    private const double MaxDurationToleranceSeconds = 15;

    private static readonly ConcurrentDictionary<string, FingerprintCacheEntry> FingerprintCache =
        new(StringComparer.Ordinal);

    private sealed record FingerprintCacheEntry(
        long Length,
        DateTime Mtime,
        FingerprintResult? Fingerprint,
        long AccessedTicks);

    private readonly IFingerprinter? _fingerprinter;

    public AcoustIdSource(IFingerprinter? fingerprinter) => _fingerprinter = fingerprinter;

    public override string Id => "acoustid";
    public override string DisplayName => "AcoustID";
    // AcoustID 本身只有文本；封面通过 release MBID 指向 Cover Art Archive。
    public override SourceCapability Capabilities => SourceCapability.Tags | SourceCapability.Cover;

    public override async Task<IReadOnlyList<TagCandidate>> SearchAsync(TagQuery query, int limit,
        SourceCapability capability, CancellationToken ct)
    {
        LastError = null;
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

        // AcoustID 要求传整首时长；指纹只覆盖前 120 秒，但时长必须用文件总长。
        var duration = query.Duration is { TotalSeconds: > 1 } d
            ? (int)Math.Round(d.TotalSeconds)
            : fingerprint.DurationSeconds;

        // duration 是 AcoustID 的必需参数；未知时（0）不要发送错误值，直接说明原因。
        if (duration <= 0)
        {
            LastError = "AcoustID：无法确定文件时长";
            return [];
        }

        await ThrottleAsync(ct);
        var form = BuildLookupForm(fingerprint.Fingerprint, duration);

        int statusCode;
        string json;
        try
        {
            (statusCode, json) = await Http.PostFormWithStatusAsync("https://api.acoustid.org/v2/lookup", form, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LastError = "AcoustID：请求超时";
            return [];
        }
        catch (HttpRequestException)
        {
            LastError = "AcoustID：查询失败（网络错误）";
            return [];
        }

        try
        {
            var candidates = Parse(json, Id, limit, duration, out var parseError);
            if (parseError is not null)
            {
                LastError = parseError;
                return [];
            }

            // 业务错误通常带 JSON body（已由 Parse 处理）；没有 body 的 4xx/5xx 才回落到状态码。
            if (statusCode >= 400)
            {
                LastError = $"AcoustID：查询失败（HTTP {statusCode}）";
                return [];
            }

            return candidates;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            // 解析外部接口数据属于系统边界：结构变化时给出可见的失败原因，
            // 而不是让整次查询静默变成「无结果」。服务端的原始报文不展示给用户。
            LastError = statusCode >= 400
                ? $"AcoustID：查询失败（HTTP {statusCode}）"
                : "AcoustID：服务返回了无法解析的数据";
            return [];
        }
    }

    /// <summary>
    /// AcoustID lookup 的 POST 表单。指纹很长，必须走 POST。
    /// 注意 <c>meta</c> 用空格分隔：表单编码会把字面 <c>+</c> 转成 %2B，
    /// 服务端只按空格/逗号拆分 meta，写成 "recordings+releases" 会拿不到 recordings。
    /// </summary>
    internal static KeyValuePair<string, string>[] BuildLookupForm(string fingerprint, int duration) =>
    [
        new("client", DefaultApiKey),
        new("meta", "recordings releases"),
        new("format", "json"),
        new("duration", duration.ToString(CultureInfo.InvariantCulture)),
        new("fingerprint", fingerprint),
    ];

    /// <summary>解析响应的 <c>status</c>；非 <c>ok</c> 时返回可展示的简短错误。</summary>
    private static string DescribeServiceError(JsonElement root)
    {
        string? message = null;
        int? code = null;

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
                message = messageElement.GetString();

            if (error.TryGetProperty("code", out var codeElement))
            {
                if (codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt32(out var numericCode))
                    code = numericCode;
                else if (codeElement.ValueKind == JsonValueKind.String
                    && int.TryParse(codeElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var parsedCode))
                    code = parsedCode;
            }
        }

        if (!string.IsNullOrWhiteSpace(message))
            return code is { } c ? $"AcoustID：{message}（{c}）" : $"AcoustID：{message}";
        return code is { } onlyCode ? $"AcoustID：服务返回错误 {onlyCode}" : "AcoustID：服务返回错误";
    }

    /// <summary>
    /// 解析 AcoustID 查询响应：按 result.score 排序取 recording，去重后映射为候选。
    /// 同一结果内的 recording 会包含原曲、翻唱、同长度其他曲目；与文件时长相差很小的
    /// 视为同等匹配并保持服务返回顺序，差距明显的按接近程度排到后面，
    /// 避免把明显不同长度的曲目排在真正的曲目之前。
    /// </summary>
    public static IReadOnlyList<TagCandidate> Parse(string json, string sourceId, int limit,
        int? queryDurationSeconds = null) =>
        Parse(json, sourceId, limit, queryDurationSeconds, out _);

    /// <summary>
    /// 与 <see cref="Parse(string, string, int, int?)"/> 相同，另外通过 <paramref name="error"/>
    /// 返回服务端 <c>status</c> 非 ok 时的简短提示（调用方据此设置 <c>LastError</c>）。
    /// </summary>
    internal static IReadOnlyList<TagCandidate> Parse(string json, string sourceId, int limit,
        int? queryDurationSeconds, out string? error)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        error = null;

        if (root.ValueKind != JsonValueKind.Object) return [];

        // AcoustID 出错时也返回 200，错误在 status/error 里；先判状态再取数据。
        if (root.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.String
            && !string.Equals(statusElement.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            error = DescribeServiceError(root);
            return [];
        }

        if (!root.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return [];

        // 目标时长缺失或不可用时（<=0）不启用重排，保持服务返回顺序。
        var targetDuration = queryDurationSeconds is { } target && target > 0 ? target : 0;
        var tolerance = targetDuration > 0
            ? Math.Clamp(targetDuration * DurationToleranceRatio,
                MinDurationToleranceSeconds, MaxDurationToleranceSeconds)
            : 0;

        var candidates = new List<TagCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedResults = results.EnumerateArray()
            .Where(result => result.ValueKind == JsonValueKind.Object)
            .Select(result => new
            {
                Result = result,
                Score = result.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number
                    ? scoreEl.GetDouble()
                    : 0
            })
            .OrderByDescending(item => item.Score)
            .ToList();

        foreach (var item in orderedResults)
        {
            var result = item.Result;
            if (!result.TryGetProperty("recordings", out var recordings)
                || recordings.ValueKind != JsonValueKind.Array)
                continue;
            var score = item.Score;

            var ordering = recordings.EnumerateArray()
                .Where(recording => recording.ValueKind == JsonValueKind.Object)
                .Select((recording, index) => new
                {
                    Recording = recording,
                    Index = index,
                    // 时长几乎相同（重录、翻唱、样本差异）时不算差异，保留服务返回的先后顺序。
                    Distance = tolerance > 0
                        ? Math.Max(0, Math.Abs(ReadDurationSeconds(recording) - targetDuration) - tolerance)
                        : 0
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Index);

            foreach (var entry in ordering)
            {
                var recording = entry.Recording;
                if (candidates.Count >= limit) break;
                if (ReadString(recording, "id") is not { Length: > 0 } id) continue;
                if (!seen.Add(id)) continue;

                if (ReadString(recording, "title") is not { Length: > 0 } title) continue;

                var artist = recording.TryGetProperty("artists", out var artists)
                    && artists.ValueKind == JsonValueKind.Array
                    ? string.Join(" & ", artists.EnumerateArray()
                        .Select(a => ReadString(a, "name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name)))
                    : null;

                string? album = null;
                uint? year = null;
                string? coverUrl = null;
                if (recording.TryGetProperty("releases", out var releases)
                    && releases.ValueKind == JsonValueKind.Array
                    && releases.GetArrayLength() > 0)
                {
                    var release = releases[0];
                    album = ReadString(release, "title");
                    year = ReadYear(release);

                    // AcoustID 不直接返回封面；release id 就是 MusicBrainz release MBID。
                    if (ReadString(release, "id") is { Length: > 0 } releaseId)
                        coverUrl = $"https://coverartarchive.org/release/{Uri.EscapeDataString(releaseId)}/front-500";
                }

                candidates.Add(new TagCandidate(
                    SourceId: sourceId,
                    Title: title,
                    Artist: string.IsNullOrWhiteSpace(artist) ? null : artist,
                    Album: album,
                    Year: year,
                    CoverUrl: coverUrl,
                    ReleaseId: id,
                    Detail: $"{(int)Math.Round(score * 100)}%"));
            }

            if (candidates.Count >= limit) break;
        }

        return candidates;
    }

    /// <summary>读取 recording 的时长（秒）；缺失时返回 <see cref="double.MaxValue"/> 以排在后面。</summary>
    private static double ReadDurationSeconds(JsonElement recording)
    {
        if (recording.ValueKind == JsonValueKind.Object
            && recording.TryGetProperty("duration", out var duration)
            && duration.ValueKind == JsonValueKind.Number
            && duration.TryGetDouble(out var seconds))
            return seconds;
        return double.MaxValue;
    }

    /// <summary>读取字符串字段；字段缺失或不是字符串时返回 null。</summary>
    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    /// <summary>
    /// 发行日期在 AcoustID 响应里是对象（<c>{"year":2013,"month":5,"day":17}</c>），
    /// 旧版接口曾返回字符串（<c>"2013-05-17"</c>），两种都兼容。
    /// </summary>
    private static uint? ReadYear(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object
            || !release.TryGetProperty("date", out var date))
            return null;

        if (date.ValueKind == JsonValueKind.Object)
        {
            if (date.TryGetProperty("year", out var yearEl) && yearEl.ValueKind == JsonValueKind.Number
                && yearEl.TryGetUInt32(out var numericYear))
                return numericYear;
            if (date.TryGetProperty("year", out var yearText) && yearText.ValueKind == JsonValueKind.String
                && uint.TryParse(yearText.GetString(), out var stringYear))
                return stringYear;
            return null;
        }

        if (date.ValueKind == JsonValueKind.String
            && date.GetString() is { Length: >= 4 } text
            && uint.TryParse(text[..4], out var parsed))
            return parsed;

        return null;
    }

    private static async Task<FingerprintResult?> GetFingerprintAsync(IFingerprinter fp, IFileHandle file,
        CancellationToken ct)
    {
        var mtime = file.LastWriteTimeUtc;
        if (FingerprintCache.TryGetValue(file.Key, out var hit)
            && hit.Length == file.Length && hit.Mtime == mtime)
        {
            FingerprintCache[file.Key] = hit with { AccessedTicks = Environment.TickCount64 };
            return hit.Fingerprint;
        }

        var result = await fp.ComputeAsync(file, ct);

        // 失败（null）也缓存：解码失败的文件重试代价很高。条目带 (Length, Mtime) 校验，
        // 文件被替换后会自动失效。
        Store(file.Key, new FingerprintCacheEntry(file.Length, mtime, result, Environment.TickCount64));
        return result;
    }

    private static void Store(string key, FingerprintCacheEntry entry)
    {
        FingerprintCache[key] = entry;
        if (FingerprintCache.Count < FingerprintCacheLimit) return;

        // 淘汰约 1/4 最久未使用的条目；整本清空会让正在批量处理的文件反复重新计算。
        var victims = FingerprintCache
            .OrderBy(pair => pair.Value.AccessedTicks)
            .Take(FingerprintCache.Count / 4 + 1)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var victim in victims)
            FingerprintCache.TryRemove(victim, out _);
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
