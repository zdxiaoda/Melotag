namespace Melotag.Core.Tests;

using Melotag.Core.Abstractions;
using Melotag.Core.Files;
using Melotag.Core.Models;
using Melotag.Tags;
using TL = TagLib;

public class TagLibTagStoreTests
{
    /// <summary>TagLib# 2.3 没有 .tta/.dff 的实现：认领它们会让导入直接抛 UnsupportedFormatException。</summary>
    [Theory]
    [InlineData(".tta", false)]
    [InlineData(".dff", false)]
    [InlineData(".flac", true)]
    [InlineData(".mp3", true)]
    [InlineData(".m4a", true)]
    [InlineData(".unknown", false)]
    public void CanHandle_MatchesTagLibCapabilities(string extension, bool expected)
    {
        Assert.Equal(expected, new TagLibTagStore().CanHandle(extension));
    }

    [Fact]
    public void CanHandle_IsCaseInsensitive()
    {
        Assert.True(new TagLibTagStore().CanHandle(".FLAC"));
    }

    /// <summary>
    /// 只改标题时不能把磁盘上的多值 Performers/Genres 压平成单值：
    /// 差分写入策略保证首值相同时保持原数组不动。
    /// </summary>
    [FfmpegFact]
    public void Write_OnlyTitle_MultiValuePerformersAndGenresSurvive()
    {
        var dir = Directory.CreateTempSubdirectory("melotag-multivalue-").FullName;
        try
        {
            var file = Path.Combine(dir, "sample.flac");
            Assert.True(Ffmpeg.TryCreateSample(file, ".flac"), "ffmpeg 生成测试样本失败");
            var store = new TagLibTagStore();
            var handle = new PhysicalFileHandle(file);

            // 用 TagLib 直接写入多值（应用自身只写单值，但库里可能有 "A; B"）
            using (var tl = TL.File.Create(file))
            {
                tl.Tag.Performers = ["Artist A", "Artist B"];
                tl.Tag.Genres = ["Rock", "Pop"];
                tl.Tag.AlbumArtists = ["Album Artist A", "Album Artist B"];
                tl.Save();
            }

            var loaded = store.Read(handle).Tags;
            Assert.Equal("Artist A", loaded.Artist);
            Assert.Equal("Rock", loaded.Genre);

            loaded.Title = "只改标题";
            store.Write(handle, loaded, new TagWriteOptions("2.3", KeepModifiedTime: true, WriteLyricsToTag: true));

            using (var tl = TL.File.Create(file))
            {
                Assert.Equal(new[] { "Artist A", "Artist B" }, tl.Tag.Performers);
                Assert.Equal(new[] { "Rock", "Pop" }, tl.Tag.Genres);
                Assert.Equal(new[] { "Album Artist A", "Album Artist B" }, tl.Tag.AlbumArtists);
                Assert.Equal("只改标题", tl.Tag.Title);
            }

            // 再从应用视角读一遍，首值仍然是原来那个
            var reread = store.Read(new PhysicalFileHandle(file)).Tags;
            Assert.Equal("只改标题", reread.Title);
            Assert.Equal("Artist A", reread.Artist);
            Assert.Equal("Rock", reread.Genre);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响结论 */ }
        }
    }
}
