namespace Melotag.Core.Tests;

using Melotag.Core.Services;
using SkiaSharp;

public class CoverServiceTests
{
    private static byte[] CreateImage(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] CreateJpeg(int width, int height) =>
        CreateImage(SKEncodedImageFormat.Jpeg, width, height);

    private static byte[] CreatePng(int width, int height) =>
        CreateImage(SKEncodedImageFormat.Png, width, height);

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

    [Fact]
    public void Normalize_TruncatedJpeg_ReturnsNull()
    {
        var service = new CoverService();
        var source = CreateJpeg(1600, 1200);
        var truncated = source[..(source.Length / 2)];

        // 文件头仍然可解析，但 GetPixels 会返回 IncompleteInput，不能把它当成有效封面。
        Assert.Null(service.Normalize(truncated, 800, 800, 1024, ["jpg"]));
    }

    [Fact]
    public void Normalize_TooLargePng_ReturnsNullInsteadOfFullDecode()
    {
        var service = new CoverService();
        var source = CreatePng(4100, 4000); // 16.4M 像素，超过解码上限，且 PNG 不支持缩放解码

        Assert.Null(service.Normalize(source, 800, 800, 1024, ["png"]));
    }

    [Fact]
    public void ValidateOriginal_KeepsBytesAndSize()
    {
        var service = new CoverService();
        var source = CreateJpeg(2000, 1500);

        var result = service.ValidateOriginal(source, ["jpg", "png"]);

        Assert.NotNull(result);
        Assert.Same(source, result!.Data);
        Assert.Equal(2000, result.Width);
        Assert.Equal(1500, result.Height);
        Assert.Equal("image/jpeg", result.MimeType);
    }

    [Fact]
    public void ValidateOriginal_RejectsDisallowedFormat()
    {
        var service = new CoverService();
        Assert.Null(service.ValidateOriginal(CreateJpeg(200, 200), ["png"]));
    }

    [Fact]
    public void ValidateOriginal_Garbage_ReturnsNull()
    {
        var service = new CoverService();
        Assert.Null(service.ValidateOriginal([1, 2, 3, 4], ["jpg", "png"]));
    }

    [Fact]
    public void ValidateOriginal_TruncatedJpeg_ReturnsNull()
    {
        var service = new CoverService();
        var source = CreateJpeg(1600, 1200);
        var truncated = source[..(source.Length / 2)];

        Assert.Null(service.ValidateOriginal(truncated, ["jpg"]));
    }
}
