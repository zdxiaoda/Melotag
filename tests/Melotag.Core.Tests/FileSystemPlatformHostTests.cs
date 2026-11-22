namespace Melotag.Core.Tests;

using Melotag.Core.Files;
using Melotag.Storage;

public class FileSystemPlatformHostTests : IDisposable
{
    private readonly string _dir;
    private readonly FileSystemPlatformHost _host = new();

    public FileSystemPlatformHostTests()
    {
        _dir = Directory.CreateTempSubdirectory("melotag-export-").FullName;
    }

    private string CreateAudio(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public async Task ExportCover_WritesCoverNextToAudio()
    {
        var audio = CreateAudio("song.mp3");
        byte[] data = [0xFF, 0xD8, 0xFF, 0xD9];

        var exported = await _host.ExportCoverAsync(new PhysicalFileHandle(audio), data, ".jpg");

        Assert.NotNull(exported);
        Assert.Equal(Path.Combine(_dir, "song-cover.jpg"), exported);
        Assert.Equal(data, await File.ReadAllBytesAsync(exported!));
    }

    /// <summary>
    /// 目标已存在时追加 (2)，避免静默覆盖用户已有的封面文件；
    /// 已有文件的内容必须保持不变。
    /// </summary>
    [Fact]
    public async Task ExportCover_ExistingTarget_UsesNumberedName_AndKeepsOriginal()
    {
        var audio = CreateAudio("song.mp3");
        var existing = Path.Combine(_dir, "song-cover.jpg");
        byte[] existingData = [0x01, 0x02];
        await File.WriteAllBytesAsync(existing, existingData);

        byte[] data = [0xFF, 0xD8, 0xFF, 0xD9];
        var exported = await _host.ExportCoverAsync(new PhysicalFileHandle(audio), data, ".jpg");

        Assert.Equal(Path.Combine(_dir, "song-cover (2).jpg"), exported);
        Assert.Equal(data, await File.ReadAllBytesAsync(exported!));
        Assert.Equal(existingData, await File.ReadAllBytesAsync(existing));
    }

    [Fact]
    public async Task ExportCover_RepeatedExports_KeepIncrementing()
    {
        var audio = CreateAudio("song.mp3");
        var handle = new PhysicalFileHandle(audio);
        byte[] data = [0xFF, 0xD8, 0xFF, 0xD9];

        var first = await _host.ExportCoverAsync(handle, data, ".jpg");
        var second = await _host.ExportCoverAsync(handle, data, ".jpg");
        var third = await _host.ExportCoverAsync(handle, data, ".png");

        Assert.EndsWith("song-cover.jpg", first);
        Assert.EndsWith("song-cover (2).jpg", second);
        Assert.EndsWith("song-cover.png", third);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响结论 */ }
    }
}
