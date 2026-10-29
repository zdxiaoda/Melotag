namespace Melotag.Core.Tests;

using Melotag.Core.Models;
using Melotag.Core.Rename;

public class RenameServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly RenameService _service = new();

    public RenameServiceTests()
    {
        _dir = Directory.CreateTempSubdirectory("musictag-rename-").FullName;
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void Preview_RendersPatternAndKeepsExtension()
    {
        var path = CreateFile("track.mp3");
        var tags = new TrackTags { Title = "Song", Artist = "Band", Track = 3 };

        var preview = _service.Preview(path, tags, "@5. @1 - @2", new WindowsSafeNamePolicy());

        Assert.Null(preview.Error);
        Assert.Equal("03. Song - Band.mp3", preview.TargetName);
        Assert.True(preview.IsValid);
    }

    [Fact]
    public void Preview_InvalidPattern_ReturnsError()
    {
        var path = CreateFile("track.mp3");
        var preview = _service.Preview(path, new TrackTags { Title = "x" }, "@0", new WindowsSafeNamePolicy());

        Assert.NotNull(preview.Error);
        Assert.False(preview.IsValid);
    }

    [Fact]
    public void Preview_AvoidsCollision()
    {
        var path = CreateFile("track.mp3");
        CreateFile("Song.mp3");

        var preview = _service.Preview(path, new TrackTags { Title = "Song" }, "@1", new WindowsSafeNamePolicy());

        Assert.Equal("Song (2).mp3", preview.TargetName);
    }

    [Fact]
    public void Apply_RenamesRelatedLyricsAndPictures()
    {
        var path = CreateFile("track.mp3");
        var lrc = CreateFile("track.lrc");
        var pic = CreateFile("track.jpg");

        var preview = _service.Preview(path, new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());
        var result = _service.Apply(preview, alsoRenameLyrics: true, alsoRenamePictures: true);

        Assert.True(File.Exists(Path.Combine(_dir, "New.mp3")));
        Assert.True(File.Exists(Path.Combine(_dir, "New.lrc")));
        Assert.True(File.Exists(Path.Combine(_dir, "New.jpg")));
        Assert.False(File.Exists(lrc));
        Assert.False(File.Exists(pic));
        Assert.Equal(2, result.RenamedSiblings.Count);
    }

    [Fact]
    public void Apply_SkipsRelatedFilesWhenDisabled()
    {
        var path = CreateFile("track.mp3");
        CreateFile("track.lrc");

        var preview = _service.Preview(path, new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());
        _service.Apply(preview, alsoRenameLyrics: false, alsoRenamePictures: false);

        Assert.True(File.Exists(Path.Combine(_dir, "New.mp3")));
        Assert.True(File.Exists(Path.Combine(_dir, "track.lrc")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
