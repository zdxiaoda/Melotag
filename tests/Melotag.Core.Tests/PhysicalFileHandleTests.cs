namespace Melotag.Core.Tests;

using Melotag.Core.Files;

public sealed class PhysicalFileHandleTests
{
    [Fact]
    public void RefreshMetadata_ReReadsSizeAndModificationTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"melotag-handle-{Guid.NewGuid():N}.bin");

        try
        {
            File.WriteAllBytes(path, new byte[3]);
            var handle = new PhysicalFileHandle(path);

            Assert.Equal(3, handle.Length);
            Assert.True(handle.LastWriteTimeUtc > DateTime.UnixEpoch);

            var modified = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.WriteAllBytes(path, new byte[7]);
            File.SetLastWriteTimeUtc(path, modified);

            handle.RefreshMetadata();

            Assert.Equal(7, handle.Length);
            Assert.True(Math.Abs((handle.LastWriteTimeUtc - modified).TotalSeconds) < 2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RefreshMetadata_MissingFileResetsMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"melotag-handle-{Guid.NewGuid():N}.bin");

        try
        {
            File.WriteAllBytes(path, new byte[3]);
            var handle = new PhysicalFileHandle(path);
            Assert.Equal(3, handle.Length);

            File.Delete(path);
            handle.RefreshMetadata();

            Assert.Equal(0, handle.Length);
            Assert.Equal(DateTime.UnixEpoch, handle.LastWriteTimeUtc);
            Assert.Null(handle.CreationTimeUtc);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
