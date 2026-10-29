using Android.Content;
using Android.OS;
using Android.Provider;
using Melotag.Core.Abstractions;
using Melotag.Storage;
using AndroidEnvironment = Android.OS.Environment;

namespace Melotag.Droid;

/// <summary>
/// Android 平台能力：数据目录放应用私有空间；SAF 不支持重命名与同目录兄弟文件；
/// 封面导出走 MediaStore（Android 10+），失败时回退到应用外部目录。
/// </summary>
public sealed class AndroidPlatformHost : IPlatformHost
{
    private readonly Context _context;

    public AndroidPlatformHost(Context context)
    {
        _context = context;

        var dataDir = Path.Combine(context.FilesDir!.AbsolutePath, "data");
        Directory.CreateDirectory(dataDir);
        AppPaths.OverrideDataDirectory = dataDir;
    }

    public string DataDirectory => AppPaths.DataDirectory;

    /// <summary>SAF 单文档授权下无法原地重命名。</summary>
    public bool SupportsRename => false;

    /// <summary>无法访问音频同目录的 .lrc / 封面文件。</summary>
    public bool SupportsSiblingFiles => false;

    public Task<string?> ExportCoverAsync(IFileHandle audio, byte[] data, string extension) =>
        Task.FromResult(Export(audio, data, extension));

    private string? Export(IFileHandle audio, byte[] data, string extension)
    {
        var mime = extension == ".png" ? "image/png" : "image/jpeg";
        var name = audio.FileNameWithoutExtension + "-cover" + extension;
        var resolver = _context.ContentResolver!;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            try
            {
                var values = new ContentValues();
                values.Put(MediaStore.MediaColumns.DisplayName, name);
                values.Put(MediaStore.MediaColumns.MimeType, mime);
                values.Put(MediaStore.MediaColumns.RelativePath, AndroidEnvironment.DirectoryPictures + "/Melotag");

                var collection = MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary);
                var uri = resolver.Insert(collection, values);
                if (uri is not null)
                {
                    using (var output = resolver.OpenOutputStream(uri))
                        output?.Write(data);

                    return "Pictures/Melotag/" + name;
                }
            }
            catch
            {
                // 回退到应用外部目录
            }
        }

        try
        {
            var root = _context.GetExternalFilesDir(AndroidEnvironment.DirectoryPictures)?.AbsolutePath
                       ?? _context.FilesDir!.AbsolutePath;
            var dir = Path.Combine(root, "Melotag");
            Directory.CreateDirectory(dir);

            var target = Path.Combine(dir, name);
            File.WriteAllBytes(target, data);
            return target;
        }
        catch
        {
            return null;
        }
    }
}
