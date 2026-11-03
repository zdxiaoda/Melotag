namespace Melotag.Core.Abstractions;

/// <summary>
/// 平台能力差异集中在这里，避免 Core/UI 里散落 <c>OperatingSystem.IsAndroid()</c> 判断。
/// </summary>
public interface IPlatformHost
{
    /// <summary>配置 / 历史数据库所在目录。</summary>
    string DataDirectory { get; }

    /// <summary>
    /// 导出封面。桌面写音频同目录的 "文件名-cover.jpg"；Android 写入媒体库并返回可读位置说明。
    /// 无法导出时返回 null。
    /// </summary>
    Task<string?> ExportCoverAsync(IFileHandle audio, byte[] data, string extension);
}
