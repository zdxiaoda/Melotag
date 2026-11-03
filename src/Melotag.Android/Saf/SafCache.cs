namespace Melotag.Droid;

using System.Security.Cryptography;
using System.Text;
using Android.Content;
using AndroidUri = Android.Net.Uri;

/// <summary>SAF 文档在应用私有目录中的缓存路径。</summary>
internal static class SafCache
{
    public static string PathFor(Context context, AndroidUri uri, string displayName) =>
        Path.Combine(context.FilesDir!.AbsolutePath, "mediacache", StableHash(uri.ToString()!), Sanitize(displayName));

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
