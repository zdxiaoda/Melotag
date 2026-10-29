namespace Melotag.Core.Tests;

using System.Diagnostics;
using Melotag.Core.Abstractions;
using Melotag.Core.Files;
using Melotag.Core.Models;
using Melotag.Tags;

public class TagLibRoundTripTests
{
    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool TryCreateSample(string ffmpeg, string output, string extension)
    {
        var codec = extension switch
        {
            ".mp3" => "libmp3lame",
            ".flac" => "flac",
            _ => null,
        };
        if (codec is null) return false;

        var args = $"-hide_banner -loglevel error -y -f lavfi -i anullsrc=r=44100:cl=stereo -t 0.5 -c:a {codec} \"{output}\"";
        using var process = Process.Start(new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (process is null) return false;
        process.WaitForExit(30_000);
        return process.ExitCode == 0 && File.Exists(output);
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    public void WriteThenRead_RoundTripsAllMappedFields(string extension)
    {
        var ffmpeg = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (ffmpeg is null) return;

        var dir = Directory.CreateTempSubdirectory("musictag-tags-").FullName;
        var file = Path.Combine(dir, "sample" + extension);
        if (!TryCreateSample(ffmpeg, file, extension)) return;

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

    [Fact]
    public void Write_KeepModifiedTime_PreservesTimestamp()
    {
        var ffmpeg = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (ffmpeg is null) return;

        var dir = Directory.CreateTempSubdirectory("musictag-mtime-").FullName;
        var file = Path.Combine(dir, "sample.flac");
        if (!TryCreateSample(ffmpeg, file, ".flac")) return;

        var store = new TagLibTagStore();
        var handle = new PhysicalFileHandle(file);
        var before = handle.LastWriteTimeUtc;

        store.Write(handle, new TrackTags { Title = "x" }, new TagWriteOptions(KeepModifiedTime: true));

        var after = new PhysicalFileHandle(file).LastWriteTimeUtc;
        Assert.True((after - before).Duration() < TimeSpan.FromSeconds(2),
            $"修改时间未被保留：{before:O} -> {after:O}");
    }

    [Fact]
    public void Write_CoverPicture_RoundTrips()
    {
        var ffmpeg = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (ffmpeg is null) return;

        var dir = Directory.CreateTempSubdirectory("musictag-cover-").FullName;
        var file = Path.Combine(dir, "sample.mp3");
        if (!TryCreateSample(ffmpeg, file, ".mp3")) return;

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
    }
}
