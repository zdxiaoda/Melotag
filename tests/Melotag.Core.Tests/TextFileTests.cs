namespace Melotag.Core.Tests;

using System.Text;
using Melotag.Core.Lyrics;

public class TextFileTests
{
    public TextFileTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Decode_Utf8Bom()
    {
        var bytes = new UTF8Encoding(true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes("歌词"))
            .ToArray();

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("歌词", text);
        Assert.Equal("utf-8-bom", name);
    }

    [Fact]
    public void Decode_Gb18030()
    {
        var bytes = Encoding.GetEncoding(54936).GetBytes("中文歌词");

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("中文歌词", text);
        Assert.Equal("gb18030", name);
    }

    [Fact]
    public void Decode_PlainUtf8()
    {
        var bytes = new UTF8Encoding(false).GetBytes("[00:01.00]hello");

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("[00:01.00]hello", text);
        Assert.Equal("utf-8", name);
    }

    [Fact]
    public void GetEncoding_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextFile.GetEncoding("nonsense"));
    }

    /// <summary>
    /// 保存歌词时句柄是同一个可写流：先写长文本再写短文本，不截断就会留下上一次的尾巴，
    /// 用户看到的歌词会掺进旧内容。
    /// </summary>
    [Fact]
    public void WriteAllText_ShorterTextOnSameStream_DropsOldTail()
    {
        using var stream = new MemoryStream();

        TextFile.WriteAllText(stream, "这是一行很长很长的旧歌词 old lyrics tail", "utf-8");
        TextFile.WriteAllText(stream, "短", "utf-8");

        var reread = ReadFromStart(stream, out var encodingName);

        Assert.Equal("短", reread);
        Assert.Equal("utf-8", encodingName);
        Assert.Equal(Encoding.UTF8.GetByteCount("短"), stream.Length);
    }

    [Fact]
    public void WriteAllText_LongerTextOnSeekableStream_DoesNotOverlapOldBytes()
    {
        using var stream = new MemoryStream();

        TextFile.WriteAllText(stream, "abc", "utf-8");
        TextFile.WriteAllText(stream, "abcdef", "utf-8");

        Assert.Equal("abcdef", ReadFromStart(stream, out _));
    }

    /// <summary><see cref="TextFile.ReadAllText(Stream, out string)"/> 从当前位置读，读之前必须回到开头。</summary>
    private static string ReadFromStart(Stream stream, out string encodingName)
    {
        stream.Position = 0;
        return TextFile.ReadAllText(stream, out encodingName);
    }

    [Fact]
    public void Decode_Utf32LeBom()
    {
        var bytes = new UTF32Encoding(false, true).GetPreamble()
            .Concat(new UTF32Encoding(false, false).GetBytes("歌词"))
            .ToArray();

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("歌词", text);
        Assert.Equal("utf-32", name);
    }

    [Fact]
    public void Decode_Utf32BeBom()
    {
        var bytes = new UTF32Encoding(true, true).GetPreamble()
            .Concat(new UTF32Encoding(true, false).GetBytes("歌词"))
            .ToArray();

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("歌词", text);
        Assert.Equal("utf-32-be", name);
    }

    /// <summary>
    /// 落盘路径（File.WriteAllText 会带 BOM）必须能往返 UTF-16BE。
    /// 注意：写流的重载不带 BOM（GetBytes 不含 preamble），因此无 BOM 的 UTF-16BE 无法自动识别。
    /// </summary>
    [Fact]
    public void WriteThenDecode_Utf16Be_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "melotag-utf16be-" + Guid.NewGuid().ToString("N") + ".lrc");
        try
        {
            TextFile.WriteAllText(path, "[00:01.00]hello 歌", "utf-16-be");

            var text = TextFile.ReadAllText(path, out var name);

            Assert.Equal("[00:01.00]hello 歌", text);
            Assert.Equal("utf-16-be", name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>没有 BOM 的 UTF-16LE（部分老工具输出）必须靠「奇数位大量 NUL」认出来。</summary>
    [Fact]
    public void Decode_BomlessUtf16Le_IsDetected()
    {
        var bytes = new UnicodeEncoding(false, false).GetBytes("[00:01.00]hello");

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("[00:01.00]hello", text);
        Assert.Equal("utf-16", name);
    }

    [Fact]
    public void Decode_BomlessUtf16Le_ChineseText_IsDetected()
    {
        var bytes = new UnicodeEncoding(false, false).GetBytes("[00:01.00]中文歌词");

        var text = TextFile.Decode(bytes, out var name);

        Assert.Equal("[00:01.00]中文歌词", text);
        Assert.Equal("utf-16", name);
    }

    [Theory]
    [InlineData("utf8", 65001)]
    [InlineData("UTF-8", 65001)]
    [InlineData("utf-8bom", 65001)]
    [InlineData("utf16", 1200)]
    [InlineData("utf-16le", 1200)]
    [InlineData("utf-32", 12000)]
    [InlineData("utf-32-be", 12001)]
    [InlineData("gbk", 54936)]
    [InlineData("gb2312", 54936)]
    [InlineData("cp936", 54936)]
    [InlineData("big5", 950)]
    [InlineData("cp950", 950)]
    public void GetEncoding_HistoricalAliases_Resolve(string name, int codePage)
    {
        Assert.Equal(codePage, TextFile.GetEncoding(name).CodePage);
    }

    [Fact]
    public void GetEncoding_Utf8BomAlias_HasPreamble()
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, TextFile.GetEncoding("utf-8bom").GetPreamble());
        Assert.Empty(TextFile.GetEncoding("utf8").GetPreamble());
    }
}
