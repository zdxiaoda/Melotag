namespace Melotag.Core.Tests;

using Melotag.Core.Abstractions;
using Melotag.Core.Files;
using Melotag.Core.Models;
using Melotag.Tags;

public class TagLibRoundTripTests
{
    [FfmpegTheory]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    public void WriteThenRead_RoundTripsAllMappedFields(string extension)
    {
        var dir = Directory.CreateTempSubdirectory("musictag-tags-").FullName;
        var file = Path.Combine(dir, "sample" + extension);
        Assert.True(Ffmpeg.TryCreateSample(file, extension), "ffmpeg 生成测试样本失败");

        var store = new TagLibTagStore();
        var handle = new PhysicalFileHandle(file);

        var initial = store.Read(handle);
        Assert.Equal(TrackStatus.Loaded, initial.Status);
        Assert.False(string.IsNullOrWhiteSpace(initial.Info?.Format));

        var tags = new TrackTags
        {
            Title = "标题",
            Artist = "艺术家",
            Album = "专辑",
            AlbumArtist = "合辑艺术家",
            Year = 2024,
            Track = 7,
            Disc = 1,
            Genre = "Rock",
            Comment = "备注",
            Composer = "作曲",
            Lyrics = "[00:01.00]hello",
        };

        store.Write(handle, tags, new TagWriteOptions("2.3", KeepModifiedTime: true, WriteLyricsToTag: true));

        var actual = store.Read(new PhysicalFileHandle(file)).Tags;

        Assert.Equal(tags.Title, actual.Title);
        Assert.Equal(tags.Artist, actual.Artist);
        Assert.Equal(tags.Album, actual.Album);
        Assert.Equal(tags.Year, actual.Year);
        Assert.Equal(tags.Track, actual.Track);
        Assert.Equal(tags.Disc, actual.Disc);
        Assert.Equal(tags.Genre, actual.Genre);
        Assert.Equal(tags.Comment, actual.Comment);
        Assert.Equal(tags.Lyrics, actual.Lyrics);
    }

    [FfmpegFact]
    public void Write_KeepModifiedTime_PreservesTimestamp()
    {
        var dir = Directory.CreateTempSubdirectory("musictag-mtime-").FullName;
        var file = Path.Combine(dir, "sample.flac");
        Assert.True(Ffmpeg.TryCreateSample(file, ".flac"), "ffmpeg 生成测试样本失败");

        var store = new TagLibTagStore();
        var handle = new PhysicalFileHandle(file);
        var before = handle.LastWriteTimeUtc;

        store.Write(handle, new TrackTags { Title = "x" }, new TagWriteOptions(KeepModifiedTime: true));

        var after = new PhysicalFileHandle(file).LastWriteTimeUtc;
        Assert.True((after - before).Duration() < TimeSpan.FromSeconds(2),
            $"修改时间未被保留：{before:O} -> {after:O}");
    }

    [FfmpegFact]
    public void Write_CoverPicture_RoundTrips()
    {
        var dir = Directory.CreateTempSubdirectory("musictag-cover-").FullName;
        var file = Path.Combine(dir, "sample.mp3");
        Assert.True(Ffmpeg.TryCreateSample(file, ".mp3"), "ffmpeg 生成测试样本失败");

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9 };
        var store = new TagLibTagStore();
        store.Write(new PhysicalFileHandle(file), new TrackTags
        {
            Title = "cover",
            Pictures = [new TagPicture(jpeg, "image/jpeg", PictureKind.FrontCover, "front")],
        }, new TagWriteOptions(ReplacePictures: true));

        var tags = store.Read(new PhysicalFileHandle(file)).Tags;

        Assert.Single(tags.Pictures);
        Assert.Equal("image/jpeg", tags.Pictures[0].MimeType);
        Assert.Equal(jpeg, tags.Pictures[0].Data);

        var withoutPictures = store.Read(new PhysicalFileHandle(file), includePictures: false);
        Assert.True(withoutPictures.HasCover);
        Assert.Empty(withoutPictures.Tags.Pictures);
        Assert.NotNull(withoutPictures.ModifiedUtc);

        var pictures = store.ReadPictures(new PhysicalFileHandle(file));
        Assert.Single(pictures);
        Assert.Equal(jpeg, pictures[0].Data);
    }

    [FfmpegFact]
    public void Read_EmptyPictureFrame_DoesNotReportCover()
    {
        var dir = Directory.CreateTempSubdirectory("musictag-cover-empty-").FullName;
        var file = Path.Combine(dir, "sample.mp3");
        Assert.True(Ffmpeg.TryCreateSample(file, ".mp3"), "ffmpeg 生成测试样本失败");

        var store = new TagLibTagStore();
        store.Write(new PhysicalFileHandle(file), new TrackTags
        {
            Pictures = [new TagPicture([], "image/jpeg")],
        }, new TagWriteOptions(ReplacePictures: true));

        var result = store.Read(new PhysicalFileHandle(file), includePictures: false);

        Assert.False(result.HasCover);
        Assert.Empty(store.ReadPictures(new PhysicalFileHandle(file)));
    }

    [FfmpegFact]
    public void Write_WithoutReplacingPictures_KeepsExistingCover()
    {
        var dir = Directory.CreateTempSubdirectory("musictag-cover-keep-").FullName;
        var file = Path.Combine(dir, "sample.mp3");
        Assert.True(Ffmpeg.TryCreateSample(file, ".mp3"), "ffmpeg 生成测试样本失败");

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9 };
        var store = new TagLibTagStore();
        store.Write(new PhysicalFileHandle(file), new TrackTags
        {
            Title = "before",
            Pictures = [new TagPicture(jpeg, "image/jpeg", PictureKind.FrontCover, "front")],
        }, new TagWriteOptions(ReplacePictures: true));

        // 模拟「没改封面直接保存」：标签里不带图片，且 ReplacePictures = false。
        var tags = new TrackTags { Title = "after" };
        store.Write(new PhysicalFileHandle(file), tags, new TagWriteOptions(ReplacePictures: false));

        var result = store.Read(new PhysicalFileHandle(file));
        Assert.Equal("after", result.Tags.Title);
        Assert.True(result.HasCover);
        Assert.Single(result.Tags.Pictures);
        Assert.Equal(jpeg, result.Tags.Pictures[0].Data);
    }
}
