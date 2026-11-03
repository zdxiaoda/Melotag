namespace Melotag.Droid;

using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Avalonia.Controls;
using Melotag.Core.Abstractions;
using Melotag.UI.Platform;
using AndroidUri = Android.Net.Uri;

/// <summary>Android 文件选择：ACTION_OPEN_DOCUMENT 多选 / ACTION_OPEN_DOCUMENT_TREE 目录树。</summary>
public sealed class SafDocumentPicker : IFilePickerPlatform
{
    private static readonly string[] Projection =
    [
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
        DocumentsContract.Document.ColumnFlags,
    ];

    public static SafDocumentPicker Instance { get; } = new();

    public bool SupportsFolderPick
    {
        get
        {
            var activity = MainActivity.Current;
            if (activity?.PackageManager is not { } manager) return false;

            var probe = new Intent(Intent.ActionOpenDocumentTree);
            return manager.ResolveActivity(probe, (PackageInfoFlags)0) is not null;
        }
    }

    public async Task<IReadOnlyList<IFileHandle>> PickFilesAsync(TopLevel top)
    {
        var activity = MainActivity.Current;
        if (activity is null) return [];

        var uris = await activity.PickDocumentsAsync();
        if (uris.Count == 0) return [];

        return await Task.Run(() => ToHandles(activity, uris));
    }

    public async Task<IReadOnlyList<IFileHandle>> PickFolderAsync(TopLevel top, Func<string, bool>? filter = null)
    {
        var activity = MainActivity.Current;
        if (activity is null) return [];

        AndroidUri? treeUri;
        try
        {
            treeUri = await activity.PickFolderAsync();
        }
        catch
        {
            return [];
        }

        if (treeUri is null) return [];

        var resolver = activity.ContentResolver!;
        return await Task.Run(() => SafTree.Enumerate(resolver, treeUri, filter)
            .Select(document => (IFileHandle)new SafFileHandle(activity, resolver, document))
            .ToList());
    }

    private static IReadOnlyList<IFileHandle> ToHandles(Context context, IReadOnlyList<AndroidUri> uris)
    {
        var resolver = context.ContentResolver!;
        var results = new List<IFileHandle>();

        foreach (var uri in uris)
        {
            if (QueryDocument(context, uri) is { } document)
                results.Add(new SafFileHandle(context, resolver, document));
        }

        return results;
    }

    private static SafDocument? QueryDocument(Context context, AndroidUri uri)
    {
        var resolver = context.ContentResolver!;
        string? name = null;
        long size = 0;
        long modified = 0;
        var flags = default(DocumentContractFlags);

        try
        {
            using var cursor = resolver.Query(uri, Projection, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var nameIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
                if (nameIndex >= 0) name = cursor.GetString(nameIndex);

                var sizeIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnSize);
                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex)) size = cursor.GetLong(sizeIndex);

                var modifiedIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnLastModified);
                if (modifiedIndex >= 0 && !cursor.IsNull(modifiedIndex)) modified = cursor.GetLong(modifiedIndex);

                var flagsIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnFlags);
                if (flagsIndex >= 0 && !cursor.IsNull(flagsIndex))
                    flags = (DocumentContractFlags)cursor.GetInt(flagsIndex);
            }
        }
        catch
        {
            // 有些 provider 不支持查询列，用默认值
        }

        if (string.IsNullOrWhiteSpace(name)) name = "audio";

        return new SafDocument(
            uri,
            DocumentsContract.GetDocumentId(uri)!,
            name!,
            size,
            modified,
            flags.HasFlag(DocumentContractFlags.SupportsWrite),
            flags.HasFlag(DocumentContractFlags.SupportsRename),
            TreeUri: null);
    }
}
