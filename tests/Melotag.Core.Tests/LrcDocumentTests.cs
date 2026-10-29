namespace Melotag.Core.Tests;

using Melotag.Core.Lyrics;

public class LrcDocumentTests
{
    [Fact]
    public void Parse_KeepsMultipleTimestampsAndText()
    {
        var doc = LrcDocument.Parse("[00:12.34][00:15.00]hello");

        Assert.Single(doc.Lines);
        Assert.Equal(2, doc.Lines[0].Times.Count);
        Assert.Equal("hello", doc.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(12.34), doc.Lines[0].Times[0]);
    }

    [Fact]
    public void ReformatTimeTags_SplitsAndSorts()
    {
        var doc = LrcDocument.Parse("[00:15.00][00:12.34]hello");
        doc.ReformatTimeTags();

        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(12.34), doc.Lines[0].Times[0]);
        Assert.Equal(TimeSpan.FromSeconds(15.00), doc.Lines[1].Times[0]);
    }

    [Fact]
    public void RemoveTimeTags_KeepsText()
    {
        var doc = LrcDocument.Parse("[00:01.00]line");
        doc.RemoveTimeTags();

        Assert.Empty(doc.Lines[0].Times);
        Assert.Equal("line", doc.Lines[0].Text);
    }

    [Fact]
    public void Shift_ClampsAtZero()
    {
        var doc = LrcDocument.Parse("[00:01.00]a");
        doc.Shift(TimeSpan.FromSeconds(-5));

        Assert.Equal(TimeSpan.Zero, doc.Lines[0].Times[0]);
    }

    [Fact]
    public void RemoveHeadTags_RemovesMetadataOnly()
    {
        var doc = LrcDocument.Parse("[ti:Title]\n[offset:0]\n[00:01.00]line");
        doc.RemoveHeadTags();

        Assert.Single(doc.Lines);
        Assert.Equal("line", doc.Lines[0].Text);
    }

    [Fact]
    public void RemoveBlankLines_DropsEmptyAndWhitespace()
    {
        var doc = LrcDocument.Parse("[00:01.00]a\n\n[00:02.00]   \n[00:03.00]b");
        doc.RemoveBlankLines();

        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void MergeTranslation_AppendsByTimestamp()
    {
        var original = LrcDocument.Parse("[00:01.00]hello\n[00:02.00]world");
        var translated = LrcDocument.Parse("[00:01.00]你好\n[00:02.00]世界");

        original.MergeTranslation(translated, " / ");

        Assert.Equal("hello / 你好", original.Lines[0].Text);
        Assert.Equal("world / 世界", original.Lines[1].Text);
    }

    [Fact]
    public void ToLrcText_UsesTwoDigitHundredths()
    {
        var doc = LrcDocument.Parse("[00:01.5]a\n[01:02.34]b");
        var text = doc.ToLrcText();

        Assert.Equal("[00:01.50]a\n[01:02.34]b", text);
    }
}
