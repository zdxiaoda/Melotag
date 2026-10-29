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
    public void GetEncoding_Unknown_FallsBackToUtf8()
    {
        Assert.IsType<UTF8Encoding>(TextFile.GetEncoding("nonsense"));
    }
}
