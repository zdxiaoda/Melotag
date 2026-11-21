namespace Melotag.Desktop.Audio;

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

            return Finish(chroma, fedFrames, format.SampleRate);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LastError = $"AcoustID 无法解码：{ex.Message}";
            return null;
        }
    }

    private static FingerprintResult? Finish(AcoustID.ChromaContext chroma, long fedFrames, int sampleRate)
    {
        chroma.Finish();
        var fingerprint = chroma.GetFingerprint();
        if (string.IsNullOrEmpty(fingerprint))
            return null;
        return new FingerprintResult(fingerprint, (int)(fedFrames / sampleRate));
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
                try
                {
                    var source = gst_element_factory_make("filesrc", "src");
                    var decode = gst_element_factory_make("decodebin", "decode");
                    var convert = gst_element_factory_make("audioconvert", "convert");
                    var resample = gst_element_factory_make("audioresample", "resample");
                    var sink = gst_element_factory_make("appsink", "sink");
                    if (source == 0 || decode == 0 || convert == 0 || resample == 0 || sink == 0)
                    {
                        onError("AcoustID 不可用：系统 GStreamer 组件不完整");
                        return null;
                    }

                    bin = gst_element_factory_make("pipeline", null);
                    SetString(source, "location", path);

                    gst_bin_add(bin, source);
                    gst_bin_add(bin, decode);
                    gst_bin_add(bin, convert);
                    gst_bin_add(bin, resample);
                    gst_bin_add(bin, sink);
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
                    SetCaps(sink, "caps", caps);
                    gst_caps_unref(caps);
                    caps = 0;

                    // decodebin 的输出 pad 动态出现，只连接音频 pad。
                    var state = new PadState { ConvertSink = gst_element_get_static_pad(convert, "sink") };
                    sinkPad = state.ConvertSink;
                    var handle = GCHandle.Alloc(state);
                    padHandle = handle;
                    g_signal_connect_data(decode, "pad-added",
                        Marshal.GetFunctionPointerForDelegate(PadAdded), GCHandle.ToIntPtr(handle), 0, 0);

                    bus = gst_element_get_bus(bin);
                    if (gst_element_set_state(bin, StatePlaying) < 0)
                    {
                        onError("AcoustID：GStreamer 无法启动解码");
                        return null;
                    }

                    return Pull(sink, bus, ct, onError);
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
                    padHandle?.Free();
                    if (bin != 0) gst_object_unref(bin);
                }
            }
        }

        private static FingerprintResult? Pull(nint sink, nint bus, CancellationToken ct, Action<string> onError)
        {
            var chroma = new AcoustID.ChromaContext();
            if (!chroma.Start(TargetRate, TargetChannels))
            {
                onError("AcoustID：指纹初始化失败");
                return null;
            }

            var fedFrames = 0L;
            var maxFrames = (long)TargetRate * MaxSeconds;
            var buffer = new byte[256 * 1024];
            while (fedFrames < maxFrames)
            {
                if (ct.IsCancellationRequested) return null;

                var sample = gst_app_sink_try_pull_sample(sink, PullTimeoutNs);
                if (sample != 0)
                {
                    try
                    {
                        var gstBuffer = gst_sample_get_buffer(sample);
                        var size = gstBuffer == 0 ? 0 : gst_buffer_get_size(gstBuffer);
                        if (size > 0)
                        {
                            var toCopy = (nint)Math.Min((long)size, buffer.Length);
                            var copied = (int)gst_buffer_extract(gstBuffer, 0, buffer, toCopy);
                            var usable = copied & ~1;
                            if (usable > 0)
                            {
                                var samples = new short[usable / 2];
                                Buffer.BlockCopy(buffer, 0, samples, 0, usable);
                                chroma.Feed(samples, samples.Length);
                                fedFrames += samples.Length / TargetChannels;
                            }
                        }
                    }
                    finally
                    {
                        gst_sample_unref(sample);
                    }
                    continue;
                }

                if (gst_app_sink_is_eos(sink)) break;

                // 无样本也非 EOS：查总线错误（缺解码插件等）。
                var message = gst_bus_timed_pop_filtered(bus, PullTimeoutNs, MessageError);
                if (message != 0)
                {
                    try
                    {
                        gst_message_parse_error(message, out var error, out _);
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
                }
            }

            if (fedFrames == 0)
            {
                onError("AcoustID：文件中没有可解码的音频轨");
                return null;
            }

            chroma.Finish();
            var fingerprint = chroma.GetFingerprint();
            return string.IsNullOrEmpty(fingerprint)
                ? null
                : new FingerprintResult(fingerprint, (int)(fedFrames / TargetRate));
        }

        private sealed class PadState
        {
            public nint ConvertSink;
        }

        private static void OnPadAdded(nint element, nint pad, nint userData)
        {
            var state = (PadState)GCHandle.FromIntPtr(userData).Target!;
            var caps = gst_pad_get_current_caps(pad);
            var isAudio = false;
            if (caps != 0)
            {
                var structure = gst_caps_get_structure(caps, 0);
                if (structure != 0)
                {
                    var name = Marshal.PtrToStringAnsi(gst_structure_get_name(structure));
                    isAudio = name == "audio/x-raw";
                }
                gst_caps_unref(caps);
            }
            else
            {
                // 无 caps 时也尝试连接，由协商阶段决定成败。
                isAudio = true;
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
