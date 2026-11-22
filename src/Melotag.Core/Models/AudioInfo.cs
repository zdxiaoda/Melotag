namespace Melotag.Core.Models;

/// <summary>来自标签引擎（时长/码率/采样率/声道）与文件系统的技术信息。</summary>
public sealed record AudioInfo(
    string? Format,
    TimeSpan Duration,
    int? BitrateKbps,
    int? SampleRateHz,
    int? Channels,
    long SizeBytes)
{
    /// <summary>时长显示。用 TotalHours 而不是 "h" 格式，避免超过 24 小时回绕成 0 点。</summary>
    public string DurationText
    {
        get
        {
            if (Duration.TotalHours >= 1)
                return $"{(int)Duration.TotalHours}:{Duration.Minutes:00}:{Duration.Seconds:00}";

            return $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";
        }
    }

    public string BitrateText => BitrateKbps is null ? "" : $"{BitrateKbps} kbps";
    public string SampleRateText => SampleRateHz is null ? "" : $"{SampleRateHz} Hz";
    public string ChannelsText => Channels is null ? "" : $"{Channels} ch";
    public string SizeText => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.#} KB"
        : $"{SizeBytes / 1024.0 / 1024.0:0.##} MB";
}
