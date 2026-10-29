namespace Melotag.UI.Platform;

using Avalonia.Controls;
using Melotag.Core.Abstractions;

/// <summary>平台文件选择。桌面走 Avalonia StorageProvider；Android 走 SAF。</summary>
public interface IFilePickerPlatform
{
    /// <summary>Android 的 SAF 目录选择不保证可递归枚举，视平台而定。</summary>
    bool SupportsFolderPick { get; }

    Task<IReadOnlyList<IFileHandle>> PickFilesAsync(TopLevel top);

    Task<IReadOnlyList<IFileHandle>> PickFolderAsync(TopLevel top);
}

public static class PlatformServices
{
    /// <summary>平台能力；桌面默认由 Melotag.Storage.FileSystemPlatformHost 提供。</summary>
    public static IPlatformHost Host { get; set; } = new Storage.FileSystemPlatformHost();

    public static IFilePickerPlatform FilePicker { get; set; } = new StorageProviderFilePicker();
}
