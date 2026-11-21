namespace Melotag.Droid.Audio;

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
    private const long AnalyzeSeconds = 120;

    // MediaCodec.Info* 常量（避免绑定枚举名差异）。
    private const int InfoTryAgainLater = -1;
    private const int InfoOutputFormatChanged = -2;

    // AudioFormat.ENCODING_PCM_16BIT / ENCODING_PCM_FLOAT。
    private const int Pcm16Bit = 2;
    private const int PcmFloat = 4;

    private readonly Context _context;

    public AndroidFingerprinter(Context context) => _context = context;

    public bool IsAvailable => true;

    public string? LastError { get; private set; }

    public Task<FingerprintResult?> ComputeAsync(IFileHandle file, CancellationToken ct) =>
        Task.Run(() => Compute(file, ct), CancellationToken.None);

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

            return Decode(fd, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LastError = $"AcoustID：无法解码该文件（{ex.Message}）";
            return null;
        }
    }

    private FingerprintResult? Decode(Java.IO.FileDescriptor fd, CancellationToken ct)
    {
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(fd);

        var trackIndex = -1;
        MediaFormat? format = null;
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var candidate = extractor.GetTrackFormat(i);
            if (candidate.GetString(MediaFormat.KeyMime) is not { } mime
                || !mime.StartsWith("audio/", StringComparison.Ordinal)) continue;

            trackIndex = i;
            format = candidate;
            break;
        }

        if (trackIndex < 0 || format is null) return null;
        extractor.SelectTrack(trackIndex);

        using var codec = MediaCodec.CreateDecoderByType(format.GetString(MediaFormat.KeyMime)!);
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();

        var sampleRate = format.GetInteger(MediaFormat.KeySampleRate);
        var channels = Math.Max(1, format.GetInteger(MediaFormat.KeyChannelCount));
        var pcmEncoding = Pcm16Bit;

        var chroma = new ChromaContext();
        var fedSamples = 0L;
        var maxSamples = sampleRate * AnalyzeSeconds;
        var inputDone = false;
        var outputDone = false;
        var info = new MediaCodec.BufferInfo();
        const long TimeoutUs = 10_000;

        // OutputFormatChanged 一般会先到，但个别解码器直接出数据；先按声明参数启动一次。
        if (!chroma.Start(sampleRate, Math.Min(channels, 2))) return null;

        while (!outputDone)
        {
            if (ct.IsCancellationRequested) return null;

            if (!inputDone)
            {
                int inIndex = (int)codec.DequeueInputBuffer(TimeoutUs);
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

            int outIndex = (int)codec.DequeueOutputBuffer(info, TimeoutUs);
            if (outIndex == InfoTryAgainLater)
            {
                continue;
            }
            if (outIndex == InfoOutputFormatChanged)
            {
                // 解码器输出格式一般在此确认；重采样参数以输出为准。
                var outputFormat = codec.OutputFormat;
                sampleRate = outputFormat.GetInteger(MediaFormat.KeySampleRate);
                channels = Math.Max(1, outputFormat.GetInteger(MediaFormat.KeyChannelCount));
                if (outputFormat.ContainsKey(MediaFormat.KeyPcmEncoding))
                    pcmEncoding = outputFormat.GetInteger(MediaFormat.KeyPcmEncoding);
                maxSamples = sampleRate * AnalyzeSeconds;
                if (!chroma.Start(sampleRate, Math.Min(channels, 2))) return null;
                continue;
            }
            if (outIndex < 0)
            {
                continue;
            }

            var output = codec.GetOutputBuffer(outIndex);
            if (output is not null && info.Size > 0)
            {
                var bytes = new byte[info.Size];
                output.Get(bytes);
                var samples = pcmEncoding == PcmFloat
                    ? FloatToShort(bytes)
                    : BytesToShort(bytes);
                fedSamples = Feed(chroma, samples, channels, fedSamples, maxSamples);
            }

            var endOfStream = info.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream);
            codec.ReleaseOutputBuffer(outIndex, false);
            if (fedSamples >= maxSamples || endOfStream) outputDone = true;
        }

        chroma.Finish();
        var fingerprint = chroma.GetFingerprint();
        return string.IsNullOrEmpty(fingerprint)
            ? null
            : new FingerprintResult(fingerprint, (int)(fedSamples / Math.Max(1, sampleRate)));
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
