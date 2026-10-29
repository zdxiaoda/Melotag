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
    public string DurationText => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public string BitrateText => BitrateKbps is null ? "" : $"{BitrateKbps} kbps";
    public string SampleRateText => SampleRateHz is null ? "" : $"{SampleRateHz} Hz";
    public string ChannelsText => Channels is null ? "" : $"{Channels} ch";
    public string SizeText => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.#} KB"
        : $"{SizeBytes / 1024.0 / 1024.0:0.##} MB";
}
