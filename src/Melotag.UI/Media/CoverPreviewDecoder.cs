namespace Melotag.UI.Media;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

/// <summary>
/// 把封面解码为受尺寸限制的位图，供详情预览、移动端缩略图和封面对话框共用。
/// 只会分配目标尺寸（以及编码器支持的一次中间尺寸）的像素，不会先解出整张原图。
/// </summary>
internal static class CoverPreviewDecoder
{
    /// <summary>单次解码的最大像素数（约 64 MB 的 BGRA 位图），超限时放弃预览。</summary>
    private const long MaxDecodePixels = 16_000_000;

    public static Bitmap? Decode(byte[] data, int maxWidth, int maxHeight)
    {
        if (data.Length == 0 || maxWidth <= 0 || maxHeight <= 0) return null;

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return null;

            var (targetWidth, targetHeight) = Fit(info.Width, info.Height, maxWidth, maxHeight);
            var desiredScale = Math.Min((float)targetWidth / info.Width, (float)targetHeight / info.Height);

            var (decodeWidth, decodeHeight) = ResolveDecodeSize(codec, info, desiredScale);
            if (decodeWidth <= 0 || decodeHeight <= 0) return null;

            using var decoded = Decode(codec, decodeWidth, decodeHeight);
            if (decoded is null) return null;

            SKBitmap? resized = null;
            try
            {
                var source = decoded;
                if (decoded.Width != targetWidth || decoded.Height != targetHeight)
                {
                    resized = Resize(decoded, targetWidth, targetHeight);
                    if (resized is null) return null;
                    source = resized;
                }

                // Avalonia 的指针构造函数会复制像素；复制完成后即可释放 Skia 位图。
                return new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul,
                    source.GetPixels(),
                    new PixelSize(source.Width, source.Height),
                    new Vector(96, 96),
                    source.RowBytes);
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 求一个既满足目标尺寸上限、又不超 <see cref="MaxDecodePixels"/> 的解码尺寸。
    /// 编解码器的采样档位给不出足够小的尺寸时继续降档：宁可解出小图，也不直接放弃预览。
    /// 返回值超出像素上限（编解码器不支持这么小的采样）时返回 (0, 0)，由调用方放弃。
    /// </summary>
    private static (int Width, int Height) ResolveDecodeSize(SKCodec codec, SKImageInfo info, float desiredScale)
    {
        var scale = Math.Clamp(desiredScale, 0.01f, 1f);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var supported = codec.GetScaledDimensions(scale);
            var width = supported.Width > 0 ? Math.Min(info.Width, supported.Width) : info.Width;
            var height = supported.Height > 0 ? Math.Min(info.Height, supported.Height) : info.Height;

            if (width <= 0 || height <= 0) return (0, 0);
            if ((long)width * height <= MaxDecodePixels) return (width, height);

            // 按超出比例再降一档采样；编解码器若忽略请求，下一轮会拿到同样的尺寸并最终放弃。
            var ratio = Math.Sqrt((double)MaxDecodePixels / ((double)width * height));
            var next = Math.Max(1f / Math.Max(info.Width, info.Height), scale * (float)ratio);
            if (next >= scale) break;
            scale = next;
        }

        return (0, 0);
    }

    private static SKBitmap? Decode(SKCodec codec, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        try
        {
            var pixels = bitmap.GetPixels();
            if (pixels == IntPtr.Zero)
            {
                bitmap.Dispose();
                return null;
            }

            // GetPixels 能区分 IncompleteInput/ErrorInInput，避免把损坏图片当成可显示封面。
            var result = codec.GetPixels(info, pixels);
            if (result == SKCodecResult.Success)
                return bitmap;

            bitmap.Dispose();
            return null;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            return null;
        }
    }

    private static SKBitmap? Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Bgra8888, SKAlphaType.Premul);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static (int Width, int Height) Fit(int width, int height, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return (
            Math.Clamp((int)Math.Floor(width * scale), 1, maxWidth),
            Math.Clamp((int)Math.Floor(height * scale), 1, maxHeight));
    }
}
