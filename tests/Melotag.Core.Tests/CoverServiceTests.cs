namespace Melotag.Core.Tests;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Melotag.Core.Services;

public class CoverServiceTests
{
    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    [Fact]
    public void Normalize_ResizesToLimit()
    {
        var service = new CoverService();
        var source = CreateJpeg(2000, 1500);

        var result = service.Normalize(source, 800, 800, 2048, ["jpg", "png"]);

        Assert.NotNull(result);
        Assert.True(result!.Width <= 800);
        Assert.True(result.Height <= 800);
        Assert.Equal("image/jpeg", result.MimeType);
    }

    [Fact]
    public void Normalize_RejectsDisallowedFormat()
    {
        var service = new CoverService();
        var source = CreateJpeg(200, 200);

        Assert.Null(service.Normalize(source, 800, 800, 2048, ["png"]));
    }

    [Fact]
    public void Normalize_RespectsSizeBudget()
    {
        var service = new CoverService();
        var source = CreateJpeg(1400, 1400);

        var result = service.Normalize(source, 1400, 1400, 64, ["jpg"]);

        Assert.NotNull(result);
        Assert.True(result!.Data.Length <= 64 * 1024);
    }

    [Fact]
    public void Normalize_Garbage_ReturnsNull()
    {
        var service = new CoverService();
        Assert.Null(service.Normalize([1, 2, 3, 4], 800, 800, 1024, ["jpg", "png"]));
    }
}
