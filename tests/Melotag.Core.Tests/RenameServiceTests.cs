namespace Melotag.Core.Tests;

using Melotag.Core.Abstractions;
using Melotag.Core.Files;
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

        var preview = _service.Preview(new PhysicalFileHandle(path), tags, "@5. @1 - @2", new WindowsSafeNamePolicy());

        Assert.Null(preview.Error);
        Assert.Equal("03. Song - Band.mp3", preview.TargetName);
        Assert.True(preview.IsValid);
    }

    [Fact]
    public void Preview_InvalidPattern_ReturnsError()
    {
        var path = CreateFile("track.mp3");
        var preview = _service.Preview(
            new PhysicalFileHandle(path),
            new TrackTags { Title = "x" },
            "@0",
            new WindowsSafeNamePolicy());

        Assert.NotNull(preview.Error);
        Assert.False(preview.IsValid);
    }

    [Fact]
    public void Preview_AvoidsCollision()
    {
        var path = CreateFile("track.mp3");
        CreateFile("Song.mp3");

        var preview = _service.Preview(
            new PhysicalFileHandle(path),
            new TrackTags { Title = "Song" },
            "@1",
            new WindowsSafeNamePolicy());

        Assert.Equal("Song (2).mp3", preview.TargetName);
    }

    [Fact]
    public void Preview_NotRenameable_ReturnsError()
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "track.mp3" };
        var handle = new MemoryHandle(files, "track.mp3", new int[1], canRename: false);

        var preview = _service.Preview(handle, new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());

        Assert.Equal(RenameService.NotRenameableError, preview.Error);
        Assert.False(preview.IsValid);
    }

    [Fact]
    public void Preview_CachesSiblingsPerDirectory()
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "a.mp3", "b.mp3" };
        var calls = new int[1];

        _service.Preview(new MemoryHandle(files, "a.mp3", calls), new TrackTags { Title = "Song" }, "@1", new WindowsSafeNamePolicy());
        _service.Preview(new MemoryHandle(files, "b.mp3", calls), new TrackTags { Title = "Song" }, "@1", new WindowsSafeNamePolicy());

        Assert.Equal(1, calls[0]);
    }

    [Fact]
    public void Apply_RenamesRelatedLyricsAndPictures()
    {
        var path = CreateFile("track.mp3");
        var lrc = CreateFile("track.lrc");
        var pic = CreateFile("track.jpg");

        var preview = _service.Preview(new PhysicalFileHandle(path), new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());
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

        var preview = _service.Preview(new PhysicalFileHandle(path), new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());
        _service.Apply(preview, alsoRenameLyrics: false, alsoRenamePictures: false);

        Assert.True(File.Exists(Path.Combine(_dir, "New.mp3")));
        Assert.True(File.Exists(Path.Combine(_dir, "track.lrc")));
    }

    [Fact]
    public void Apply_UsesHandleRename()
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "track.mp3", "track.lrc", "track.jpg",
        };
        var calls = new int[1];
        var handle = new MemoryHandle(files, "track.mp3", calls);

        var preview = _service.Preview(handle, new TrackTags { Title = "New" }, "@1", new WindowsSafeNamePolicy());
        var result = _service.Apply(preview, alsoRenameLyrics: true, alsoRenamePictures: true);

        Assert.Equal("New.mp3", result.New.FileName);
        Assert.Contains("New.lrc", files);
        Assert.Contains("New.jpg", files);
        Assert.Equal(2, result.RenamedSiblings.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class MemoryHandle : IFileHandle
    {
        private readonly SortedSet<string> _files;
        private readonly int[] _siblingCalls;
        private readonly bool _canRename;

        public MemoryHandle(SortedSet<string> files, string fileName, int[] siblingCalls, bool canRename = true)
        {
            _files = files;
            _siblingCalls = siblingCalls;
            _canRename = canRename;
            FileName = fileName;
        }

        public string DisplayPath => FileName;
        public string FileName { get; }
        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FileName);
        public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
        public long Length => 0;
        public DateTime LastWriteTimeUtc => DateTime.UnixEpoch;
        public string Key => FileName;
        public bool IsWritable => true;
        public bool CanRename => _canRename;
        public string? DirectoryKey => "memory";

        public Stream OpenRead() => new MemoryStream();
        public Stream OpenReadWrite() => new MemoryStream();

        public IReadOnlyList<IFileHandle> GetSiblings()
        {
            _siblingCalls[0]++;
            return _files
                .Select(name => (IFileHandle)new MemoryHandle(_files, name, _siblingCalls, _canRename))
                .ToList();
        }

        public IFileHandle Rename(string newFileName)
        {
            _files.Remove(FileName);
            _files.Add(newFileName);
            return new MemoryHandle(_files, newFileName, _siblingCalls, _canRename);
        }

        public void SetLastWriteTimeUtc(DateTime utc)
        {
        }
    }
}
