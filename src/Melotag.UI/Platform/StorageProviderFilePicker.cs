namespace Melotag.UI.Platform;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Melotag.Core.Abstractions;
using Melotag.Core.Files;

/// <summary>桌面 / 浏览器实现：Avalonia StorageProvider + 本地路径。</summary>
public sealed class StorageProviderFilePicker : IFilePickerPlatform
{
    public static readonly FilePickerFileType AudioFileType = new("音频文件")
    {
        Patterns =
        [
            "*.mp3", "*.flac", "*.ape", "*.wv", "*.tta", "*.mpc",
            "*.ogg", "*.opus", "*.m4a", "*.mp4", "*.wav", "*.aif", "*.aiff",
            "*.wma", "*.dsf", "*.dff",
        ],
    };

    public bool SupportsFolderPick => true;

    public async Task<IReadOnlyList<IFileHandle>> PickFilesAsync(TopLevel top)
    {
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择音频文件",
            AllowMultiple = true,
            FileTypeFilter = [AudioFileType, FilePickerFileTypes.All],
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => (IFileHandle)new PhysicalFileHandle(p!))
            .ToList();
    }

    public async Task<IReadOnlyList<IFileHandle>> PickFolderAsync(TopLevel top)
    {
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择音乐目录",
            AllowMultiple = false,
        });

        var result = new List<IFileHandle>();
        foreach (var folder in folders)
        {
            var path = folder.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                result.Add(new PhysicalFileHandle(file));
        }

        return result;
    }
}
