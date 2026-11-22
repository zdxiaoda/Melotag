namespace Melotag.Desktop.Audio;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Melotag.Core.Abstractions;
using Melotag.Core.Files;

/// <summary>
/// 桌面端指纹：优先使用平台自带解码组件，不随包携带任何二进制。
/// Windows 走系统 Media Foundation（MP3/M4A/AAC/FLAC/WMA/ALAC 等）；
/// Linux 走系统 GStreamer（filesrc → decodebin → audioconvert → appsink），
/// 缺解码插件时把系统报错原样上浮，提示用户自行安装。
/// 指纹计算用 AcoustID.NET（chromaprint 纯 C# 移植，与官方逐位一致）。
/// </summary>
public sealed class DesktopFingerprinter : IFingerprinter
{
    private const int TargetRate = 44100;
    private const int TargetChannels = 2;
    private const int MaxSeconds = 120;

    public string? LastError { get; private set; }

    public bool IsAvailable =>
        OperatingSystem.IsWindows() || GstInterop.IsAvailable;

    public Task<FingerprintResult?> ComputeAsync(IFileHandle file, CancellationToken ct) =>
        Task.Run(() => Compute(file, ct), ct);

    private FingerprintResult? Compute(IFileHandle file, CancellationToken ct)
    {
        LastError = null;
        if (file is not PhysicalFileHandle physical)
        {
            LastError = "AcoustID：不支持的文件来源";
            return null;
        }

        return OperatingSystem.IsWindows()
            ? DecodeMediaFoundation(physical.Key, ct)
            : GstInterop.Decode(physical.Key, ct, error => LastError = error);
    }

    /// <summary>Windows：系统 Media Foundation 解码 → PCM → AcoustID.NET。</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private FingerprintResult? DecodeMediaFoundation(string path, CancellationToken ct)
    {
        try
        {
            using var reader = new NAudio.Wave.MediaFoundationReader(path);
            var format = reader.WaveFormat;
            var channels = Math.Clamp(format.Channels, 1, 2);
            var isFloat = format.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat
                || format.BitsPerSample == 32 && format.Encoding == NAudio.Wave.WaveFormatEncoding.Extensible;

            // 注：AcoustID.NET 1.3.3 的 AcoustID.ChromaContext 是纯托管实现（只有
            // AcoustID.Native.NativeChromaContext 实现 IDisposable），没有需要释放的
            // native 资源，因此这里无需（也无法）using/Dispose。
            var chroma = new AcoustID.ChromaContext();
            if (!chroma.Start(format.SampleRate, channels))
            {
                LastError = "AcoustID：指纹初始化失败";
                return null;
            }

            var fedFrames = 0L;
            var maxFrames = (long)format.SampleRate * MaxSeconds;
            var bytesPerFrame = format.Channels * format.BitsPerSample / 8;
            var buffer = new byte[64 * 1024];
            var converter = new ShortConverter(format.Channels, channels);
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (converter.Feed(chroma, buffer, read, isFloat, out var frames)) fedFrames += frames;
                if (fedFrames >= maxFrames) break;
            }

            // AcoustID 查询要求整首时长，指纹片段只取前 120 秒，不能拿片段长度充当时长。
            // MediaFoundationReader.TotalTime 取不到时长时是 TimeSpan.Zero，即约定的“未知”。
            var totalSeconds = (int)Math.Round(reader.TotalTime.TotalSeconds);
            return Finish(chroma, totalSeconds);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LastError = $"AcoustID 无法解码：{ex.Message}";
            return null;
        }
    }

    private static FingerprintResult? Finish(AcoustID.ChromaContext chroma, int totalSeconds)
    {
        chroma.Finish();
        var fingerprint = chroma.GetFingerprint();
        if (string.IsNullOrEmpty(fingerprint))
            return null;
        // 0 表示时长未知：容器/解码器给不出整首时长时按契约返回 0，不再用片段长度顶替。
        return new FingerprintResult(fingerprint, Math.Max(totalSeconds, 0));
    }

    /// <summary>按声道缩混到 ≤2 声道并喂给指纹器；返回是否成功。</summary>
    private sealed class ShortConverter
    {
        private readonly int _sourceChannels;
        private readonly int _targetChannels;

        public ShortConverter(int sourceChannels, int targetChannels)
        {
            _sourceChannels = Math.Max(sourceChannels, 1);
            _targetChannels = Math.Clamp(targetChannels, 1, 2);
        }

        public bool Feed(AcoustID.ChromaContext chroma, byte[] bytes, int length, bool isFloat, out long frames)
        {
            frames = 0;
            short[] samples;
            if (isFloat)
            {
                var floats = new byte[length & ~3];
                Array.Copy(bytes, floats, floats.Length);
                var src = new float[floats.Length / 4];
                Buffer.BlockCopy(floats, 0, src, 0, floats.Length);
                samples = new short[src.Length];
                for (var i = 0; i < src.Length; i++)
                    samples[i] = (short)Math.Clamp(src[i] * short.MaxValue, short.MinValue, short.MaxValue);
            }
            else
            {
                var usable = length & ~1;
                samples = new short[usable / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, usable);
            }

            if (_sourceChannels == _targetChannels)
            {
                chroma.Feed(samples, samples.Length);
                frames = samples.Length / _targetChannels;
                return true;
            }

            // 多声道缩混为 2 声道（平均）；升声道不出现（目标固定 ≥ 源裁剪值）。
            var sourceFrames = samples.Length / _sourceChannels;
            if (sourceFrames == 0) return false;
            if (_targetChannels == 2)
            {
                var mixed = new short[sourceFrames * 2];
                for (var f = 0; f < sourceFrames; f++)
                {
                    var sum = 0L;
                    for (var c = 0; c < _sourceChannels; c++) sum += samples[f * _sourceChannels + c];
                    var avg = (short)(sum / _sourceChannels);
                    mixed[f * 2] = avg;
                    mixed[f * 2 + 1] = avg;
                }
                chroma.Feed(mixed, mixed.Length);
                frames = sourceFrames;
                return true;
            }

            chroma.Feed(samples, samples.Length);
            frames = sourceFrames;
            return true;
        }
    }

    /// <summary>
    /// GStreamer 最小互操作：filesrc → decodebin → audioconvert → audioresample →
    /// appsink（S16LE / 44100Hz / 双声道），appsink 拉取模式无需 GLib 主循环。
    /// 元素都在 gst-plugins-base，缺的只是具体解码插件（由报错提示安装）。
    /// </summary>
    private static class GstInterop
    {
        // 运行时精确 soname：无 -dev 包的系统上不存在不带版本的 .so 软链。
        private const string Gst = "libgstreamer-1.0.so.0";
        private const string App = "libgstapp-1.0.so.0";
        private const string GObject = "libgobject-2.0.so.0";
        private const string Glib = "libglib-2.0.so.0";

        private const int StateNull = 1;
        private const int StatePlaying = 4;
        private const uint MessageError = 1 << 1;
        private const ulong PullTimeoutNs = 100_000_000; // 100ms
        private const int FormatTime = 3; // GstFormat: TIME（查询结果为纳秒）

        /// <summary>整条管线的墙钟上限：正常解码 120 秒音频远快于此，超出说明卡死或无音频轨。</summary>
        private static readonly TimeSpan DecodeBudget = TimeSpan.FromSeconds(MaxSeconds * 1.5 + 30);

        /// <summary>连续多久既没有样本、也没有 EOS、也没有总线消息，就判定为卡死。</summary>
        private static readonly TimeSpan StallBudget = TimeSpan.FromSeconds(5);

        private const string TimeoutMessage = "AcoustID：解码超时（文件可能不含音频轨）";

        private static bool? _available;
        private static readonly Lock InitLock = new();

        [StructLayout(LayoutKind.Sequential)]
        private struct GValue
        {
            public nint Type;
            public nint Data1;
            public nint Data2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GError
        {
            public uint Domain;
            public int Code;
            public nint Message;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PadAddedHandler(nint element, nint pad, nint userData);

        private static readonly PadAddedHandler PadAdded = OnPadAdded;

        // gstreamer-1.0
        [DllImport(Gst)] private static extern void gst_init(nint argc, nint argv);
        [DllImport(Gst)] private static extern nint gst_element_factory_make(string factoryName, string? name);
        [DllImport(Gst)] private static extern bool gst_bin_add(nint bin, nint element);
        [DllImport(Gst)] private static extern bool gst_element_link(nint src, nint sink);
        [DllImport(Gst)] private static extern int gst_element_set_state(nint element, int state);
        [DllImport(Gst)] private static extern bool gst_element_query_duration(nint element, int format, out long duration);
        [DllImport(Gst)] private static extern nint gst_element_get_bus(nint element);
        [DllImport(Gst)] private static extern nint gst_element_get_static_pad(nint element, string name);
        [DllImport(Gst)] private static extern bool gst_pad_link(nint pad, nint otherPad);
        [DllImport(Gst)] private static extern nint gst_pad_get_current_caps(nint pad);
        [DllImport(Gst)] private static extern nint gst_caps_get_structure(nint caps, uint index);
        [DllImport(Gst)] private static extern nint gst_structure_get_name(nint structure);
        [DllImport(Gst)] private static extern void gst_caps_unref(nint caps);
        [DllImport(Gst)] private static extern nint gst_caps_from_string(string caps);
        [DllImport(Gst)] private static extern nint gst_sample_get_buffer(nint sample);
        [DllImport(Gst)] private static extern nint gst_buffer_get_size(nint buffer);
        [DllImport(Gst)] private static extern nint gst_buffer_extract(nint buffer, ulong offset, byte[] dest, nint size);
        [DllImport(Gst)] private static extern nint gst_bus_timed_pop_filtered(nint bus, ulong timeout, uint types);
        [DllImport(Gst)] private static extern void gst_message_parse_error(nint message, out nint error, out nint debug);
        [DllImport(Gst)] private static extern void gst_message_unref(nint message);
        [DllImport(Gst)] private static extern void gst_object_unref(nint obj);
        [DllImport(Gst)] private static extern void gst_sample_unref(nint sample);

        // gstapp-1.0
        [DllImport(App)] private static extern nint gst_app_sink_try_pull_sample(nint appsink, ulong timeout);
        [DllImport(App)] private static extern bool gst_app_sink_is_eos(nint appsink);

        // gobject-2.0 / glib-2.0
        [DllImport(GObject)] private static extern nint g_type_from_name(string name);
        [DllImport(GObject)] private static extern void g_object_set_property(nint obj, string name, ref GValue value);
        [DllImport(GObject)] private static extern nint g_signal_connect_data(
            nint instance, string signal, nint handler, nint data, nint destroy, uint connectFlags);

        [DllImport(Glib)] private static extern void g_error_free(nint error);
        [DllImport(Glib)] private static extern void g_free(nint memory);

        public static bool IsAvailable
        {
            get
            {
                if (_available is { } cached) return cached;
                lock (InitLock)
                {
                    if (_available is { } cached2) return cached2;
                    _available = Probe();
                    return _available.Value;
                }
            }
        }

        private static bool Probe()
        {
            try
            {
                gst_init(nint.Zero, nint.Zero);
                foreach (var factory in new[] { "filesrc", "decodebin", "audioconvert", "audioresample", "appsink" })
                {
                    var element = gst_element_factory_make(factory, null);
                    if (element == nint.Zero) return false;
                    gst_object_unref(element);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>解码并计算指纹；失败时把可操作的错误写入 <paramref name="onError"/>。</summary>
        public static FingerprintResult? Decode(string path, CancellationToken ct, Action<string> onError)
        {
            if (!IsAvailable)
            {
                onError("AcoustID 不可用：未找到系统 GStreamer（请安装 gstreamer1.0 及 gstreamer1.0-libav）");
                return null;
            }

            lock (InitLock)
            {
                nint bin = 0, sinkPad = 0, caps = 0, bus = 0;
                GCHandle? padHandle = null;

                // 元素所有权：gst_bin_add 成功后归 pipeline，失败/未加入的必须自己 unref。
                var source = nint.Zero;
                var decode = nint.Zero;
                var convert = nint.Zero;
                var resample = nint.Zero;
                var sink = nint.Zero;
                var elements = new nint[5];
                var added = new bool[5];

                try
                {
                    source = gst_element_factory_make("filesrc", "src");
                    decode = gst_element_factory_make("decodebin", "decode");
                    convert = gst_element_factory_make("audioconvert", "convert");
                    resample = gst_element_factory_make("audioresample", "resample");
                    sink = gst_element_factory_make("appsink", "sink");
                    elements[0] = source;
                    elements[1] = decode;
                    elements[2] = convert;
                    elements[3] = resample;
                    elements[4] = sink;
                    if (source == 0 || decode == 0 || convert == 0 || resample == 0 || sink == 0)
                    {
                        onError("AcoustID 不可用：系统 GStreamer 组件不完整");
                        return null;
                    }

                    bin = gst_element_factory_make("pipeline", null);
                    if (bin == 0)
                    {
                        onError("AcoustID：GStreamer 管线创建失败");
                        return null;
                    }

                    SetString(source, "location", path);

                    for (var i = 0; i < elements.Length; i++)
                    {
                        if (!gst_bin_add(bin, elements[i]))
                        {
                            onError("AcoustID：GStreamer 管线构建失败");
                            return null;
                        }

                        added[i] = true;
                    }

                    if (!gst_element_link(source, decode) ||
                        !gst_element_link(convert, resample) || !gst_element_link(resample, sink))
                    {
                        onError("AcoustID：GStreamer 管线构建失败");
                        return null;
                    }

                    // appsink：关闭时钟同步、拉取模式，限定 S16LE / 44100 / 双声道输出。
                    SetBoolean(sink, "sync", false);
                    SetBoolean(sink, "emit-signals", false);
                    caps = gst_caps_from_string($"audio/x-raw,format=S16LE,layout=interleaved,rate={TargetRate},channels={TargetChannels}");
                    if (caps == 0)
                    {
                        onError("AcoustID：GStreamer 管线构建失败");
                        return null;
                    }

                    SetCaps(sink, "caps", caps);
                    gst_caps_unref(caps);
                    caps = 0;

                    // decodebin 的输出 pad 动态出现，只连接音频 pad。
                    sinkPad = gst_element_get_static_pad(convert, "sink");
                    if (sinkPad == 0)
                    {
                        onError("AcoustID：GStreamer 管线构建失败");
                        return null;
                    }

                    var state = new PadState { ConvertSink = sinkPad };
                    var handle = GCHandle.Alloc(state);
                    padHandle = handle;
                    g_signal_connect_data(decode, "pad-added",
                        Marshal.GetFunctionPointerForDelegate(PadAdded), GCHandle.ToIntPtr(handle), 0, 0);

                    bus = gst_element_get_bus(bin);
                    if (bus == 0)
                    {
                        onError("AcoustID：GStreamer 无法启动解码");
                        return null;
                    }

                    if (gst_element_set_state(bin, StatePlaying) < 0)
                    {
                        onError("AcoustID：GStreamer 无法启动解码");
                        return null;
                    }

                    return Pull(bin, sink, bus, ct, onError);
                }
                catch (DllNotFoundException)
                {
                    onError("AcoustID 不可用：未找到系统 GStreamer（请安装 gstreamer1.0 及 gstreamer1.0-libav）");
                    return null;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    onError($"AcoustID 无法解码：{ex.Message}");
                    return null;
                }
                finally
                {
                    if (bin != 0) gst_element_set_state(bin, StateNull);
                    if (bus != 0) gst_object_unref(bus);
                    if (sinkPad != 0) gst_object_unref(sinkPad);
                    if (caps != 0) gst_caps_unref(caps);
                    if (bin != 0) gst_object_unref(bin);

                    // 先拆管线（停止可能的 pad-added 回调），再放 GCHandle，避免 native 回调期间
                    // userData 句柄悬空。
                    padHandle?.Free();

                    for (var i = 0; i < elements.Length; i++)
                    {
                        if (!added[i] && elements[i] != 0) gst_object_unref(elements[i]);
                    }
                }
            }
        }

        private static FingerprintResult? Pull(nint bin, nint sink, nint bus, CancellationToken ct, Action<string> onError)
        {
            // ChromaContext 为纯托管实现，无 native 资源可释放（见 DecodeMediaFoundation 注释）。
            var chroma = new AcoustID.ChromaContext();
            if (!chroma.Start(TargetRate, TargetChannels))
            {
                onError("AcoustID：指纹初始化失败");
                return null;
            }

            var totalSeconds = 0;
            var durationQueried = false;
            var fedFrames = 0L;
            var maxFrames = (long)TargetRate * MaxSeconds;
            var buffer = new byte[256 * 1024];

            // 墙钟保险：正常解码 120 秒音频是秒级完成，卡在 Pull 里必须能退出。
            var clock = Stopwatch.StartNew();
            var lastProgress = TimeSpan.Zero;

            while (fedFrames < maxFrames)
            {
                if (ct.IsCancellationRequested) return null;
                if (clock.Elapsed > DecodeBudget)
                {
                    onError(TimeoutMessage);
                    return null;
                }

                var sample = gst_app_sink_try_pull_sample(sink, PullTimeoutNs);
                if (sample != 0)
                {
                    try
                    {
                        fedFrames = FeedSample(chroma, sample, buffer, fedFrames, maxFrames);
                    }
                    finally
                    {
                        gst_sample_unref(sample);
                    }

                    // 时长在解析出首个样本后才可靠；查不到就下一块再试，循环结束再兜底一次。
                    if (!durationQueried)
                    {
                        totalSeconds = QueryDurationSeconds(bin);
                        durationQueried = totalSeconds > 0;
                    }

                    lastProgress = clock.Elapsed;
                    continue;
                }

                if (gst_app_sink_is_eos(sink)) break;

                // 无样本也非 EOS：查总线错误（缺解码插件等）。
                var message = gst_bus_timed_pop_filtered(bus, PullTimeoutNs, MessageError);
                if (message != 0)
                {
                    try
                    {
                        gst_message_parse_error(message, out var error, out var debug);
                        if (debug != 0) g_free(debug); // debug 串由 GLib 分配，必须 g_free。

                        if (error != 0)
                        {
                            var text = Marshal.PtrToStringAnsi(
                                Marshal.PtrToStructure<GError>(error).Message) ?? "未知错误";
                            g_error_free(error);
                            onError($"AcoustID 无法解码：{text}（可安装 gstreamer1.0-libav 或对应解码插件）");
                            return null;
                        }
                    }
                    finally
                    {
                        gst_message_unref(message);
                    }

                    lastProgress = clock.Elapsed;
                    continue;
                }

                // 连续若干秒既无样本、也非 EOS、也无总线消息：管线卡死。
                if (clock.Elapsed - lastProgress > StallBudget)
                {
                    onError(TimeoutMessage);
                    return null;
                }
            }

            if (fedFrames == 0)
            {
                onError("AcoustID：文件中没有可解码的音频轨");
                return null;
            }

            // 时长兜底：解析出时长较晚的容器（如无索引的流）首块时可能还查不到。
            if (totalSeconds <= 0) totalSeconds = QueryDurationSeconds(bin);

            chroma.Finish();
            var fingerprint = chroma.GetFingerprint();
            if (string.IsNullOrEmpty(fingerprint)) return null;

            // 0 表示时长未知：不能用参与指纹的片段长度冒充整首时长。
            return new FingerprintResult(fingerprint, totalSeconds);
        }

        /// <summary>查整首时长（秒）；容器尚未解析出时长时返回 0。</summary>
        private static int QueryDurationSeconds(nint bin)
        {
            if (!gst_element_query_duration(bin, FormatTime, out var durationNs) || durationNs <= 0) return 0;
            return (int)Math.Round(durationNs / 1_000_000_000d);
        }

        /// <summary>
        /// 把整块 GstBuffer 按 chunk 全部读出来喂给指纹器（不再只取前 256KB），
        /// 并在达到 <paramref name="maxFrames"/> 时截断。返回累计已喂的帧数。
        /// </summary>
        private static long FeedSample(AcoustID.ChromaContext chroma, nint sample, byte[] buffer, long fedFrames,
            long maxFrames)
        {
            var gstBuffer = gst_sample_get_buffer(sample);
            if (gstBuffer == 0) return fedFrames;

            var total = (long)gst_buffer_get_size(gstBuffer);
            for (long offset = 0; offset < total && fedFrames < maxFrames;)
            {
                var chunk = (nint)Math.Min(total - offset, buffer.Length);
                if (chunk <= 0) break;

                var copied = (int)gst_buffer_extract(gstBuffer, (ulong)offset, buffer, chunk);
                if (copied <= 0) break;
                offset += copied;

                var usable = copied & ~1; // S16LE：丢弃尾部半个样本
                if (usable <= 0) continue;

                var pending = (maxFrames - fedFrames) * TargetChannels;
                var count = (int)Math.Min(usable / 2, pending);
                count -= count % TargetChannels; // 只喂完整帧，避免声道错位
                if (count <= 0) break;

                var samples = new short[count];
                Buffer.BlockCopy(buffer, 0, samples, 0, count * 2);
                chroma.Feed(samples, count);
                fedFrames += count / TargetChannels;
            }

            return fedFrames;
        }

        private sealed class PadState
        {
            public nint ConvertSink;
        }

        private static void OnPadAdded(nint element, nint pad, nint userData)
        {
            if (GCHandle.FromIntPtr(userData).Target is not PadState state) return;
            if (state.ConvertSink == 0) return;

            var caps = gst_pad_get_current_caps(pad);
            if (caps == 0) return; // 无 caps 的 pad 类型未知，绝不能接到音频 sink 上。

            var isAudio = false;
            try
            {
                var structure = gst_caps_get_structure(caps, 0);
                if (structure != 0)
                {
                    var name = Marshal.PtrToStringAnsi(gst_structure_get_name(structure));
                    isAudio = name == "audio/x-raw";
                }
            }
            finally
            {
                gst_caps_unref(caps);
            }

            if (isAudio) gst_pad_link(pad, state.ConvertSink);
        }

        private static void SetString(nint obj, string name, string value)
        {
            var type = g_type_from_name("gchararray");
            if (type == 0) return;
            var pointer = Marshal.StringToHGlobalAnsi(value);
            try
            {
                var gvalue = new GValue { Type = type, Data1 = pointer };
                g_object_set_property(obj, name, ref gvalue);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static void SetBoolean(nint obj, string name, bool value)
        {
            // G_TYPE_BOOLEAN（基本类型常量 20），布尔值存于第一个数据槽。
            var gvalue = new GValue { Type = 20, Data1 = value ? 1 : 0 };
            g_object_set_property(obj, name, ref gvalue);
        }

        private static void SetCaps(nint obj, string name, nint capsPtr)
        {
            var type = g_type_from_name("GstCaps");
            if (type == 0) return;
            var gvalue = new GValue { Type = type, Data1 = capsPtr };
            g_object_set_property(obj, name, ref gvalue);
        }
    }
}
