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

    /// <summary>时间标签之间允许用空格分隔（部分播放器/LRC 编辑器会这样输出）。</summary>
    [Fact]
    public void Parse_SpaceSeparatedTimestamps_KeepsBothAndText()
    {
        var doc = LrcDocument.Parse("[00:01.00] [00:02.00]hello");

        var line = Assert.Single(doc.Lines);
        Assert.Equal(2, line.Times.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), line.Times[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), line.Times[1]);
        Assert.Equal("hello", line.Text);
    }

    /// <summary>单个时间标签后面的空格属于正文，不能被当成标签分隔符吃掉。</summary>
    [Fact]
    public void Parse_SingleTimestamp_KeepsLeadingSpaceOfText()
    {
        var doc = LrcDocument.Parse("[00:01.00] hello");

        var line = Assert.Single(doc.Lines);
        Assert.Equal(" hello", line.Text);
    }

    [Fact]
    public void Parse_PositiveOffset_ShiftsWholeTimeline()
    {
        var doc = LrcDocument.Parse("[offset:+500]\n[00:01.00]a\n[00:02.00]b");

        Assert.Equal(TimeSpan.FromMilliseconds(1500), doc.Lines[1].Times[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), doc.Lines[2].Times[0]);
    }

    [Fact]
    public void Parse_NegativeOffset_ShiftsWholeTimeline()
    {
        var doc = LrcDocument.Parse("[offset:-500]\n[00:01.00]a");

        Assert.Equal(TimeSpan.FromMilliseconds(500), doc.Lines[1].Times[0]);
    }

    /// <summary>
    /// 大负偏移把最早一行顶到 0 以下时，要整体平移（等价于最早一行落到 0），
    /// 而不是逐行截断——否则前几行会挤在同一时间点，间隔信息丢失。
    /// </summary>
    [Fact]
    public void Parse_LargeNegativeOffset_PreservesRelativeSpacing()
    {
        var doc = LrcDocument.Parse("[offset:-15000]\n[00:10.00]a\n[00:20.00]b\n[00:30.00]c");

        Assert.Equal(TimeSpan.Zero, doc.Lines[1].Times[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[2].Times[0]);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[3].Times[0]);
    }

    [Fact]
    public void MergeTranslation_MultipleTimestamps_MatchesAnyTimestamp()
    {
        var original = LrcDocument.Parse("[00:01.00][00:02.00]hello");
        var translated = LrcDocument.Parse("[00:02.00]你好");

        original.MergeTranslation(translated, " / ");

        Assert.Equal("hello / 你好", original.Lines[0].Text);
    }

    [Fact]
    public void MergeTranslation_LinesWithoutTimeline_MatchByOrder()
    {
        var original = LrcDocument.Parse("[ti:Title]\nfirst\nsecond\n");
        var translated = LrcDocument.Parse("一\n二");

        original.MergeTranslation(translated, " / ");

        Assert.Equal("[ti:Title]", original.Lines[0].Text);
        Assert.Equal("first / 一", original.Lines[1].Text);
        Assert.Equal("second / 二", original.Lines[2].Text);
    }

    [Fact]
    public void MergeTranslation_DuplicateTimestamps_AreAppendedNotOverwritten()
    {
        var original = LrcDocument.Parse("[00:01.00]hello");
        var translated = LrcDocument.Parse("[00:01.00]甲\n[00:01.00]乙");

        original.MergeTranslation(translated, " / ");

        Assert.Equal("hello / 甲 / 乙", original.Lines[0].Text);
    }

    [Fact]
    public void MergeTranslation_LeavesUnmatchedLinesUntouched()
    {
        var original = LrcDocument.Parse("[00:01.00]hello\n[00:09.00]world");
        var translated = LrcDocument.Parse("[00:01.00]你好");

        original.MergeTranslation(translated, "\n");

        Assert.Equal("hello\n你好", original.Lines[0].Text);
        Assert.Equal("world", original.Lines[1].Text);
    }
}
