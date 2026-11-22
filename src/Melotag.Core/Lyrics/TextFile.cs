namespace Melotag.Core.Lyrics;

using System.Text;

/// <summary>歌词/文本文件的编码探测与读写。旧歌词常见 GBK/Big5，需 CodePages provider。</summary>
public static class TextFile
{
    /// <summary>探测可能输出的全部编码名，UI 的下拉框直接用这份清单，保证回写时 GetEncoding 能识别。</summary>
    public static IReadOnlyList<string> EncodingChoices { get; } =
    [
        "utf-8", "utf-8-bom", "utf-16", "utf-16-be", "gb18030", "big5",
    ];

    static TextFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 按名字取编码。名字来自 <see cref="EncodingChoices"/>、<see cref="Decode"/> 的探测结果或历史设置；
    /// 未知名字是编程错误，直接抛 <see cref="ArgumentException"/>，不再静默回退 UTF-8。
    /// </summary>
    public static Encoding GetEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" => new UTF8Encoding(false),
        "utf-8-bom" => new UTF8Encoding(true),
        "utf-16" => new UnicodeEncoding(false, true),
        "utf-16-be" => new UnicodeEncoding(true, true),
        "utf-32" => new UTF32Encoding(false, true),
        "utf-32-be" => new UTF32Encoding(true, true),
        // 旧版本设置/历史上写入的别名，避免升级后保存歌词直接抛异常。
        "utf8" => new UTF8Encoding(false),
        "utf-8bom" => new UTF8Encoding(true),
        "utf16" or "utf-16le" => new UnicodeEncoding(false, true),
        "gb18030" or "gbk" or "gb2312" or "cp936" => Encoding.GetEncoding(54936),
        "big5" or "cp950" => Encoding.GetEncoding(950),
        var other => throw new ArgumentException($"未知编码：{other}", nameof(name)),
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
        // BOM 必须按长度从长到短判断：UTF-32LE 的 BOM（FF FE 00 00）以 UTF-16LE 的 BOM 开头
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            encodingName = "utf-32";
            return new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encodingName = "utf-32-be";
            return new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
        }

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

        // 无 BOM 的 UTF-16LE：ASCII 文本的奇数位大量为 0，UTF-8/GBK 文本不会这样
        if (LooksLikeUtf16Le(bytes))
        {
            encodingName = "utf-16";
            return Encoding.Unicode.GetString(bytes);
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
            // GB18030 失败后基本只能是 Big5；这一步只是兜底，没有可靠的自动区分手段。
            encodingName = "big5";
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }

    /// <summary>
    /// 粗略判断无 BOM 文本是否为 UTF-16LE：只看前 512 字节，奇数位 NUL 占比达到一半即认为成立。
    /// UTF-16BE 的 NUL 落在偶数位，因此不会被误判。
    /// </summary>
    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        var samples = Math.Min(bytes.Length, 512) & ~1;
        if (samples < 4) return false;

        var zeros = 0;
        for (var i = 1; i < samples; i += 2)
        {
            if (bytes[i] == 0) zeros++;
        }

        return zeros > 0 && zeros * 2 >= samples / 2;
    }

    public static void WriteAllText(string path, string text, string encodingName) =>
        File.WriteAllText(path, text, GetEncoding(encodingName));

    public static void WriteAllText(Stream stream, string text, string encodingName)
    {
        var bytes = GetEncoding(encodingName).GetBytes(text);

        if (stream.CanSeek)
        {
            // 覆盖写：必须先截断，否则短文本会留下上一次的长尾部
            stream.Position = 0;
            stream.SetLength(bytes.Length);
        }

        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
}
