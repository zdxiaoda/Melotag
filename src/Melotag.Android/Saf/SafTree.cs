namespace Melotag.Droid;

using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

/// <summary>SAF 目录树枚举。所有查询都在后台线程调用。</summary>
internal static class SafTree
{
    private static readonly string[] Projection =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
        DocumentsContract.Document.ColumnFlags,
    ];

    /// <summary>递归枚举目录树下的文件（含子目录）。</summary>
    public static IReadOnlyList<SafDocument> Enumerate(ContentResolver resolver, AndroidUri treeUri, Func<string, bool>? filter)
    {
        var results = new List<SafDocument>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(DocumentsContract.GetTreeDocumentId(treeUri));

        while (stack.Count > 0)
        {
            var parentId = stack.Pop();
            if (!visited.Add(parentId)) continue;

            QueryChildren(resolver, treeUri, parentId, filter, results, stack);
        }

        return results;
    }

    /// <summary>枚举某个目录的直接子文件。</summary>
    public static IReadOnlyList<SafDocument> EnumerateChildren(ContentResolver resolver, AndroidUri treeUri, string parentId)
    {
        var results = new List<SafDocument>();
        QueryChildren(resolver, treeUri, parentId, filter: null, results, directories: null);
        return results;
    }

    private static void QueryChildren(
        ContentResolver resolver,
        AndroidUri treeUri,
        string parentId,
        Func<string, bool>? filter,
        List<SafDocument> results,
        Stack<string>? directories)
    {
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentId);
        using var cursor = resolver.Query(childrenUri, Projection, null, null, null);
        if (cursor is null) return;

        var idIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
        var nameIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
        var mimeIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);
        var sizeIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnSize);
        var modifiedIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnLastModified);
        var flagsIndex = cursor.GetColumnIndex(DocumentsContract.Document.ColumnFlags);

        while (cursor.MoveToNext())
        {
            var docId = idIndex >= 0 ? cursor.GetString(idIndex) : null;
            if (string.IsNullOrEmpty(docId)) continue;

            var mime = mimeIndex >= 0 ? cursor.GetString(mimeIndex) : null;
            if (mime == DocumentsContract.Document.MimeTypeDir)
            {
                directories?.Push(docId!);
                continue;
            }

            var name = nameIndex >= 0 ? cursor.GetString(nameIndex) : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (filter is not null && !filter(Path.GetExtension(name).ToLowerInvariant()))
                continue;

            var size = sizeIndex >= 0 && !cursor.IsNull(sizeIndex) ? cursor.GetLong(sizeIndex) : 0;
            var modified = modifiedIndex >= 0 && !cursor.IsNull(modifiedIndex) ? cursor.GetLong(modifiedIndex) : 0;
            var flags = flagsIndex >= 0 && !cursor.IsNull(flagsIndex) ? cursor.GetInt(flagsIndex) : 0;

            results.Add(new SafDocument(
                DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId!),
                docId!,
                name!,
                size,
                modified,
                (flags & (int)DocumentsContract.Document.FlagSupportsWrite) != 0,
                (flags & (int)DocumentsContract.Document.FlagSupportsRename) != 0,
                treeUri));
        }
    }
}
