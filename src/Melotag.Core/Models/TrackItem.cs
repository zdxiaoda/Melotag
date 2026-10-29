namespace Melotag.Core.Models;

using Melotag.Core.Abstractions;

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

    /// <summary>读取该行时使用的句柄，保存 / 重新读取时复用（SAF 下无法从路径重建）。</summary>
    public IFileHandle? Source { get; set; }

    /// <summary>历史记录 / 去重用的稳定键，缺省回落到 Path。</summary>
    public string Key => Source?.Key ?? Path;
}
