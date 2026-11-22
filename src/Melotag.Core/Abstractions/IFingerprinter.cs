namespace Melotag.Core.Abstractions;

/// <summary>音频指纹计算结果。</summary>
/// <param name="Fingerprint">Chromaprint 指纹（Base64）。</param>
/// <param name="DurationSeconds">
/// 整首音频的时长（秒），不是参与指纹计算的片段长度。AcoustID 查询要求文件总时长，
/// 与记录时长偏差过大会匹配不到，所以指纹只算前 120 秒时也要回传完整时长。
/// 容器或解码器无法提供时长（如流式容器、无索引的文件）时为 <c>0</c>，表示<b>未知</b>：
/// 调用方不得把它当作有效时长——不要写回标签时长，也不要作为 AcoustID 请求的
/// <c>duration</c> 参数，更不能把参与指纹的片段长度当成整首时长填进来。
/// </param>
public sealed record FingerprintResult(string Fingerprint, int DurationSeconds);

/// <summary>
/// 音频指纹提供方。失败时返回 null 并通过 <see cref="LastError"/> 给出
/// 用户可处理的提示（如缺少系统解码器）。
/// </summary>
public interface IFingerprinter
{
    /// <summary>平台解码组件是否可用（Windows 的 Media Foundation 恒可用；Linux 探测 GStreamer）。</summary>
    bool IsAvailable { get; }

    /// <summary>最近一次失败的原因；成功后清空。用户可见文案。</summary>
    string? LastError { get; }

    /// <summary>计算文件指纹；无法解码（格式或缺少系统解码器）时返回 null。</summary>
    Task<FingerprintResult?> ComputeAsync(IFileHandle file, CancellationToken ct);
}
