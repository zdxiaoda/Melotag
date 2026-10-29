namespace Melotag.Core.Models;

public enum TrackStatus
{
    Pending,
    Loaded,
    Modified,
    Saved,
    Failed,
    Skipped,
}

/// <summary>列表中的一行：文件标识 + 标签 + 技术信息 + 状态。</summary>
public sealed class TrackItem
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public TrackTags Tags { get; set; } = new();
    public AudioInfo? Info { get; set; }
    public TrackStatus Status { get; set; } = TrackStatus.Pending;
    public string? Error { get; set; }
}
