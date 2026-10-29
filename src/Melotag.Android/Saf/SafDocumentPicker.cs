using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Provider;
using Avalonia.Controls;
using Melotag.Core.Abstractions;
using Melotag.UI.Platform;
using AndroidUri = Android.Net.Uri;

namespace Melotag.Droid;

/// <summary>Android 文件选择：ACTION_OPEN_DOCUMENT 多选，导入到私有缓存后交给标签引擎。</summary>
public sealed class SafDocumentPicker : IFilePickerPlatform
{
    public static SafDocumentPicker Instance { get; } = new();

    /// <summary>SAF 目录树在 Android 11+ 限制较多，先用多选文件代替。</summary>
    public bool SupportsFolderPick => false;

    public async Task<IReadOnlyList<IFileHandle>> PickFilesAsync(TopLevel top)
    {
        var activity = MainActivity.Current;
        if (activity is null) return [];

        var uris = await activity.PickDocumentsAsync();
        if (uris.Count == 0) return [];

        return await Task.Run(() => Import(activity, uris));
    }

    public Task<IReadOnlyList<IFileHandle>> PickFolderAsync(TopLevel top) =>
        Task.FromResult<IReadOnlyList<IFileHandle>>([]);

    private static IReadOnlyList<IFileHandle> Import(Context context, IReadOnlyList<AndroidUri> uris)
    {
        var resolver = context.ContentResolver!;
        var results = new List<IFileHandle>();

        foreach (var uri in uris)
        {
            var (name, size) = QueryDocument(context, uri);
            if (string.IsNullOrWhiteSpace(name)) name = "audio";

            var cacheDir = Path.Combine(context.FilesDir!.AbsolutePath, "mediacache", StableHash(uri.ToString()!));
            Directory.CreateDirectory(cacheDir);
            var cachePath = Path.Combine(cacheDir, Sanitize(name));

            try
            {
                using var input = resolver.OpenInputStream(uri);
                if (input is null) continue;

                using var output = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            catch
            {
                // 单个文档读失败不影响其它文件
                continue;
            }

            results.Add(new SafFileHandle(resolver, uri, name, cachePath, size, writable: true));
        }

        return results;
    }

    private static (string Name, long Size) QueryDocument(Context context, AndroidUri uri)
    {
        var name = "audio";
        long size = 0;

        try
        {
            using var cursor = context.ContentResolver!.Query(uri, null, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var nameIndex = cursor.GetColumnIndex(OpenableColumns.DisplayName);
                if (nameIndex >= 0) name = cursor.GetString(nameIndex) ?? name;

                var sizeIndex = cursor.GetColumnIndex(OpenableColumns.Size);
                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex)) size = cursor.GetLong(sizeIndex);
            }
        }
        catch
        {
            // 有些 provider 不支持查询列，用默认值
        }

        return (name, size);
    }

    private static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string Sanitize(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name) ? "audio" : name;
    }
}
