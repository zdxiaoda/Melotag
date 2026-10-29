namespace Melotag.Core.Lyrics;

using System.Text;

/// <summary>歌词/文本文件的编码探测与读写。旧歌词常见 GBK/Big5，需 CodePages provider。</summary>
public static class TextFile
{
    static TextFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> EncodingChoices { get; } =
    [
        "utf-8", "utf-8-bom", "utf-16", "gb18030", "big5",
    ];

    public static Encoding GetEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" => new UTF8Encoding(false),
        "utf-8-bom" => new UTF8Encoding(true),
        "utf-16" => new UnicodeEncoding(false, true),
        "gb18030" => Encoding.GetEncoding(54936),
        "big5" => Encoding.GetEncoding(950),
        _ => new UTF8Encoding(false),
    };

    public static string ReadAllText(Stream stream, out string encodingName)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray(), out encodingName);
    }

    public static string ReadAllText(string path, out string encodingName) =>
        Decode(File.ReadAllBytes(path), out encodingName);

    public static string Decode(byte[] bytes, out string encodingName)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encodingName = "utf-8-bom";
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encodingName = "utf-16";
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encodingName = "utf-16-be";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            var text = strictUtf8.GetString(bytes);
            encodingName = "utf-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            // 继续尝试中文编码
        }

        try
        {
            var gb = Encoding.GetEncoding(54936, EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback);
            var text = gb.GetString(bytes);
            encodingName = "gb18030";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encodingName = "big5";
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }

    public static void WriteAllText(string path, string text, string encodingName) =>
        File.WriteAllText(path, text, GetEncoding(encodingName));

    public static void WriteAllText(Stream stream, string text, string encodingName)
    {
        var bytes = GetEncoding(encodingName).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
}
