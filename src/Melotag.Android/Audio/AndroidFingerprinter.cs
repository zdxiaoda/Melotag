namespace Melotag.Droid.Audio;

using System.Diagnostics;
using AcoustID;
using Android.Content;
using Android.Media;
using Melotag.Core.Abstractions;

/// <summary>
/// Android 端指纹：ContentResolver 打开文件描述符 → MediaCodec 平台解码 PCM →
/// AcoustID.NET 计算指纹（纯托管，与官方逐位一致），不随 APK 携带任何解码器与二进制。
/// </summary>
public sealed class AndroidFingerprinter : IFingerprinter
{
    private const int AnalyzeSeconds = 120;

    // MediaCodec.Info* 常量（避免绑定枚举名差异）。
    private const int InfoTryAgainLater = -1;
    private const int InfoOutputFormatChanged = -2;

    // AudioFormat.ENCODING_PCM_16BIT / ENCODING_PCM_FLOAT。
    private const int Pcm16Bit = 2;
    private const int PcmFloat = 4;

    /// <summary>解码墙钟上限：正常解码 120 秒音频远快于此，超出说明解码器卡死。</summary>
    private static readonly TimeSpan DecodeBudget = TimeSpan.FromSeconds(AnalyzeSeconds * 3.0 + 30);

    private const string TimeoutMessage = "AcoustID：解码超时（文件可能不含音频轨）";

    private readonly Context _context;

    public AndroidFingerprinter(Context context) => _context = context;

    public bool IsAvailable => true;

    public string? LastError { get; private set; }

    public Task<FingerprintResult?> ComputeAsync(IFileHandle file, CancellationToken ct) =>
        Task.Run(() => Compute(file, ct), ct);

    private FingerprintResult? Compute(IFileHandle file, CancellationToken ct)
    {
        LastError = null;
        if (file is not SafFileHandle saf)
        {
            LastError = "AcoustID：不支持的文件来源";
            return null;
        }

        try
        {
            using var afd = _context.ContentResolver.OpenAssetFileDescriptor(saf.Document.Uri, "r");
            if (afd is null)
            {
                LastError = "AcoustID：无法打开该文件";
                return null;
            }

            using var fd = afd.FileDescriptor;
            if (fd is null)
            {
                LastError = "AcoustID：无法打开该文件";
                return null;
            }

            return Decode(fd, afd, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LastError = $"AcoustID：无法解码该文件（{ex.Message}）";
            return null;
        }
    }

    private FingerprintResult? Decode(Java.IO.FileDescriptor fd, AssetFileDescriptor afd, CancellationToken ct)
    {
        using var extractor = new MediaExtractor();

        // SAF 打开的可能是一个容器内的子区间，只给 fd 会把整个容器当媒体文件解析。
        // API 24+ 官方推荐直接传 AssetFileDescriptor（内部处理 offset/未知长度）；
        // API 23 没有这个重载，已知长度时传 offset/length，未知长度只能退回 fd。
        var offset = afd.StartOffset;
        var length = afd.DeclaredLength;
        if (length >= 0)
            extractor.SetDataSource(fd, offset, length);
        else if (OperatingSystem.IsAndroidVersionAtLeast(24))
            extractor.SetDataSource(afd);
        else
            extractor.SetDataSource(fd);

        var trackIndex = -1;
        MediaFormat? format = null;
        string? mime = null;
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var candidate = extractor.GetTrackFormat(i);
            if (candidate is null) continue;
            if (candidate.GetString(MediaFormat.KeyMime) is not { } candidateMime) continue;
            if (!candidateMime.StartsWith("audio/", StringComparison.Ordinal)) continue;

            trackIndex = i;
            format = candidate;
            mime = candidateMime;
            break;
        }

        if (trackIndex < 0 || format is null || mime is null) return null;
        extractor.SelectTrack(trackIndex);

        using var codec = MediaCodec.CreateDecoderByType(mime);

        // 明确请求 16bit 整型 PCM：否则部分解码器输出 float，被当成 short 喂给指纹器。
        try
        {
            format.SetInteger(MediaFormat.KeyPcmEncoding, Pcm16Bit);
        }
        catch (Exception)
        {
            // 部分平台/解码器不接受该键（或 API 级别过低），忽略即可：
            // 仍按 OutputFormat 里声明的 pcm-encoding 做转换。
        }

        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();

        var sampleRate = format.GetInteger(MediaFormat.KeySampleRate);
        var channels = Math.Max(1, format.GetInteger(MediaFormat.KeyChannelCount));

        // AcoustID 查询要整首时长（秒），而不是本次分析的 120 秒片段；拿不到时为 0（未知）。
        var totalSeconds = ReadTotalSeconds(format);

        // 官方文档要求以 getOutputFormat() 的声明为准：即使请求了 16bit，
        // 部分解码器仍可能输出 float，不能只依赖 INFO_OUTPUT_FORMAT_CHANGED。
        var pcmEncoding = Pcm16Bit;
        try
        {
            var initialFormat = codec.OutputFormat;
            if (OperatingSystem.IsAndroidVersionAtLeast(24)
                && initialFormat.ContainsKey(MediaFormat.KeyPcmEncoding))
                pcmEncoding = initialFormat.GetInteger(MediaFormat.KeyPcmEncoding);
        }
        catch (Exception)
        {
            // 个别设备在首帧前不允许查询 OutputFormat，退回请求值；格式变化时还会更新。
        }

        // ChromaContext 是纯托管实现（AcoustID.NET 1.3.3 只有 NativeChromaContext 实现
        // IDisposable），没有 native 资源需要释放。
        var chroma = new ChromaContext();
        var fedSamples = 0L;
        var maxSamples = (long)sampleRate * AnalyzeSeconds;

        // 已喂入的启动参数：OutputFormatChanged 只有参数真的变了才重启，否则保留已喂数据。
        var startedRate = 0;
        var startedChannels = 0;
        if (!StartChroma(chroma, sampleRate, channels, ref startedRate, ref startedChannels))
        {
            LastError = "AcoustID：指纹初始化失败";
            return null;
        }

        var inputDone = false;
        var outputDone = false;
        var info = new MediaCodec.BufferInfo();
        const long TimeoutUs = 10_000;

        // 墙钟上限，避免解码器卡死时永久循环。
        var clock = Stopwatch.StartNew();

        while (!outputDone)
        {
            if (ct.IsCancellationRequested)
            {
                // 用户取消不是解码错误：清掉可能残留的错误文案。
                LastError = null;
                return null;
            }

            if (clock.Elapsed > DecodeBudget)
            {
                LastError = TimeoutMessage;
                return null;
            }

            if (!inputDone)
            {
                var inIndex = (int)codec.DequeueInputBuffer(TimeoutUs);
                if (inIndex >= 0)
                {
                    var input = codec.GetInputBuffer(inIndex);
                    var size = input is null ? -1 : extractor.ReadSampleData(input, 0);
                    if (size < 0)
                    {
                        codec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                        inputDone = true;
                    }
                    else
                    {
                        codec.QueueInputBuffer(inIndex, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
                        extractor.Advance();
                    }
                }
            }

            var outIndex = (int)codec.DequeueOutputBuffer(info, TimeoutUs);
            if (outIndex == InfoTryAgainLater) continue;

            if (outIndex == InfoOutputFormatChanged)
            {
                // 解码器输出格式一般在此确认；重采样参数以输出为准。
                var outputFormat = codec.OutputFormat;
                var newRate = outputFormat.GetInteger(MediaFormat.KeySampleRate);
                var newChannels = Math.Max(1, outputFormat.GetInteger(MediaFormat.KeyChannelCount));
                // KEY_PCM_ENCODING 是 API 24+ 的键；低版本不访问，避免绑定字段缺失。
                if (OperatingSystem.IsAndroidVersionAtLeast(24)
                    && outputFormat.ContainsKey(MediaFormat.KeyPcmEncoding))
                    pcmEncoding = outputFormat.GetInteger(MediaFormat.KeyPcmEncoding);

                // 只有采样率/声道真的变化才重启指纹器，否则保留已经喂入的数据。
                if (newRate != startedRate || Math.Min(newChannels, 2) != startedChannels)
                {
                    if (!StartChroma(chroma, newRate, newChannels, ref startedRate, ref startedChannels))
                    {
                        LastError = "AcoustID：指纹初始化失败";
                        return null;
                    }

                    // 重启会丢掉此前喂入的数据，计数同步归零。
                    fedSamples = 0;
                }

                sampleRate = newRate;
                channels = newChannels;
                maxSamples = (long)sampleRate * AnalyzeSeconds;
                continue;
            }

            if (outIndex < 0) continue;

            var output = codec.GetOutputBuffer(outIndex);
            if (output is not null && info.Size > 0)
            {
                // 显式从 info.Offset 读 info.Size 字节：直接 Get(bytes) 会读到 buffer 当前位置，
                // 多数解码器的首块输出都带非 0 offset（codec-config 数据）。
                var bytes = new byte[info.Size];
                output.Get(bytes, info.Offset, info.Size);

                var samples = pcmEncoding == PcmFloat
                    ? FloatToShort(bytes)
                    : BytesToShort(bytes);
                fedSamples = Feed(chroma, samples, channels, fedSamples, maxSamples);
            }

            var endOfStream = info.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream);
            codec.ReleaseOutputBuffer(outIndex, false);
            if (fedSamples >= maxSamples || endOfStream) outputDone = true;
        }

        if (fedSamples == 0) return null;

        chroma.Finish();
        var fingerprint = chroma.GetFingerprint();
        if (string.IsNullOrEmpty(fingerprint)) return null;

        // 0 表示时长未知：不能用参与指纹的片段长度冒充整首时长。
        return new FingerprintResult(fingerprint, totalSeconds);
    }

    /// <summary>
    /// 按当前解码参数启动指纹器；参数与会话内已启动的一致时不重启（保留已喂数据）。
    /// </summary>
    private static bool StartChroma(ChromaContext chroma, int sampleRate, int channels, ref int startedRate,
        ref int startedChannels)
    {
        var target = Math.Min(channels, 2);
        if (startedRate == sampleRate && startedChannels == target) return true;

        if (!chroma.Start(sampleRate, target)) return false;

        startedRate = sampleRate;
        startedChannels = target;
        return true;
    }

    /// <summary>
    /// 从轨道格式读整首时长，四舍五入到秒。部分容器（如流式传送的 MP3）不含 durationUs，
    /// 此时返回 0，表示时长未知——调用方不得把它当作有效时长。
    /// </summary>
    private static int ReadTotalSeconds(MediaFormat format)
    {
        if (!format.ContainsKey(MediaFormat.KeyDuration)) return 0;

        var micros = format.GetLong(MediaFormat.KeyDuration);
        return micros > 0 ? (int)Math.Round(micros / 1_000_000d) : 0;
    }

    /// <summary>按声道缩混到 2 声道后喂给指纹器；超出的部分截断。</summary>
    private static long Feed(ChromaContext chroma, short[] samples, int channels, long fed, long max)
    {
        var targetChannels = Math.Min(channels, 2);

        short[] payload = samples;
        if (channels > 2)
        {
            var frames = samples.Length / channels;
            payload = new short[frames * 2];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0L;
                for (var channel = 0; channel < channels; channel++)
                    sum += samples[frame * channels + channel];
                var average = (short)(sum / channels);
                payload[frame * 2] = average;
                payload[frame * 2 + 1] = average;
            }
        }

        // 只喂完整帧，避免声道错位（解码块长度不保证是声道数的整数倍）。
        var usable = payload.Length - payload.Length % targetChannels;
        if (usable <= 0) return fed;
        if (usable != payload.Length) payload = payload[..usable];

        var remaining = (max - fed) * targetChannels;
        if (payload.Length > remaining)
            payload = remaining > 0 ? payload[..(int)remaining] : [];

        if (payload.Length > 0)
        {
            chroma.Feed(payload, payload.Length);
            fed += payload.Length / targetChannels;
        }

        return fed;
    }

    private static short[] BytesToShort(byte[] bytes)
    {
        var length = bytes.Length & ~1;
        var samples = new short[length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, length);
        return samples;
    }

    private static short[] FloatToShort(byte[] bytes)
    {
        var length = bytes.Length & ~3;
        var floats = new float[length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, length);

        var samples = new short[floats.Length];
        for (var i = 0; i < floats.Length; i++)
        {
            var clamped = Math.Clamp(floats[i], -1f, 1f);
            samples[i] = (short)(clamped * short.MaxValue);
        }

        return samples;
    }
}
