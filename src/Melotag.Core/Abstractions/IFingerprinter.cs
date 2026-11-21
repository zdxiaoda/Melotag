namespace Melotag.Core.Abstractions;

/// <summary>音频指纹计算结果。</summary>
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
