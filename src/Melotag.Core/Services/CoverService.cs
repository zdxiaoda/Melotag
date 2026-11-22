namespace Melotag.Core.Services;

using SkiaSharp;

public sealed record CoverResult(byte[] Data, string MimeType, int Width, int Height);

/// <summary>封面校验、缩放与重编码。全部在内存完成，不依赖 Windows 图形栈。</summary>
public sealed class CoverService
{
    /// <summary>
    /// 单次解码允许的最大像素数（约 64 MB 的 BGRA 位图）。
    /// JPEG 等支持缩放的格式会先按编码器支持的尺寸解码；不支持缩放的格式（PNG/BMP 等）
    /// 超过该上限时直接拒绝，避免异常大图把内存峰值放大到不可控。
    /// </summary>
    private const long MaxDecodePixels = 16_000_000;

    /// <summary>
    /// 校验格式与可解码性，原样返回图片字节（不缩放、不重编码、不受体积上限约束）。
    /// 用于「插入原始封面」；仍按缩略尺寸解码一次，截断/损坏的图片不能被当成有效封面。
    /// </summary>
    public CoverResult? ValidateOriginal(byte[] data, IReadOnlyCollection<string> allowedFormats)
    {
        if (data.Length == 0) return null;

        var allowed = AllowedFormats(allowedFormats);
        if (allowed.Count == 0) return null;

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;

            var family = FamilyOf(codec.EncodedFormat);
            if (!allowed.Contains(family)) return null;

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return null;
            if (!ValidateDecodable(codec, info)) return null;

            return new CoverResult(data, MimeFor(family), info.Width, info.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 用编码器支持的小尺寸解码来发现截断/损坏图片。不能因为原图较大就只看文件头：
    /// 截断文件的文件头同样可解析，返回值会是 SKCodecResult.IncompleteInput。
    /// </summary>
    private static bool ValidateDecodable(SKCodec codec, SKImageInfo sourceInfo)
    {
        var desiredScale = Math.Min(1f, 256f / Math.Max(sourceInfo.Width, sourceInfo.Height));
        var supported = codec.GetScaledDimensions(desiredScale);
        var width = supported.Width > 0 && supported.Width < sourceInfo.Width ? supported.Width : sourceInfo.Width;
        var height = supported.Height > 0 && supported.Height < sourceInfo.Height ? supported.Height : sourceInfo.Height;
        if (width <= 0 || height <= 0) return false;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero) return false;

        var result = codec.GetPixels(info, pixels);
        return result is not (SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput or SKCodecResult.InvalidInput);
    }

    /// <summary>
    /// 归一化允许格式：空集合表示「没有允许的格式」（调用方据此拒绝），不要回落到 jpg/png；
    /// jpeg/jfif 等别名统一成 jpg，否则用户填 "jpeg" 会永远匹配不上编码器报告的 "jpg"。
    /// </summary>
    private static HashSet<string> AllowedFormats(IReadOnlyCollection<string> formats)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var format in formats)
        {
            var normalized = format.Trim().TrimStart('.').ToLowerInvariant();
            if (normalized.Length == 0) continue;
            allowed.Add(normalized is "jpeg" or "jpe" or "jfif" ? "jpg" : normalized);
        }

        return allowed;
    }

    private static string FamilyOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "jpg",
        SKEncodedImageFormat.Png => "png",
        SKEncodedImageFormat.Bmp => "bmp",
        SKEncodedImageFormat.Gif => "gif",
        SKEncodedImageFormat.Webp => "webp",
        _ => format.ToString().ToLowerInvariant(),
    };

    private static string MimeFor(string family) => family switch
    {
        "png" => "image/png",
        "bmp" => "image/bmp",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "image/jpeg",
    };

    public CoverResult? Normalize(byte[] data, int maxWidth, int maxHeight, int maxSizeKb, IReadOnlyCollection<string> allowedFormats)
    {
        if (data.Length == 0) return null;

        var allowed = AllowedFormats(allowedFormats);
        if (allowed.Count == 0) return null;

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return null;

            var family = FamilyOf(codec.EncodedFormat);
            if (!allowed.Contains(family)) return null;

            var maxW = Math.Max(64, maxWidth);
            var maxH = Math.Max(64, maxHeight);
            var scale = Math.Min(1.0, Math.Min((double)maxW / info.Width, (double)maxH / info.Height));
            var targetWidth = Math.Clamp((int)Math.Floor(info.Width * scale), 1, maxW);
            var targetHeight = Math.Clamp((int)Math.Floor(info.Height * scale), 1, maxH);

            // JPEG 等格式支持按编码器提供的比例直接解码到较小尺寸，避免先解出整张原图。
            var decodeWidth = info.Width;
            var decodeHeight = info.Height;
            if (scale < 1.0)
            {
                var supported = codec.GetScaledDimensions((float)scale);
                if (supported.Width > 0 && supported.Height > 0 &&
                    (supported.Width < info.Width || supported.Height < info.Height))
                {
                    decodeWidth = Math.Min(supported.Width, info.Width);
                    decodeHeight = Math.Min(supported.Height, info.Height);
                }
            }

            if ((long)decodeWidth * decodeHeight > MaxDecodePixels) return null;

            SKBitmap? decoded = Decode(codec, decodeWidth, decodeHeight);
            if (decoded is null) return null;
            SKBitmap bitmap = decoded;

            try
            {
                if (bitmap.Width != targetWidth || bitmap.Height != targetHeight)
                {
                    // 缩放失败就放弃这张图：返回原尺寸会写出超出用户设置上限的封面。
                    if (Resize(bitmap, targetWidth, targetHeight) is not { } resized)
                        return null;

                    bitmap.Dispose();
                    bitmap = resized;
                }

                var usePng = family == "png" && allowed.Contains("png");
                var budget = Math.Max(16, maxSizeKb) * 1024;

                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var bytes = Encode(bitmap, usePng, 85 - attempt * 15);
                    if (bytes.Length == 0) return null;
                    if (bytes.Length <= budget || attempt == 3)
                        return new CoverResult(bytes, usePng ? "image/png" : "image/jpeg", bitmap.Width, bitmap.Height);

                    if (bitmap.Width > 64 && bitmap.Height > 64)
                    {
                        if (Resize(bitmap, bitmap.Width * 4 / 5, bitmap.Height * 4 / 5) is not { } smaller)
                            return null;

                        bitmap.Dispose();
                        bitmap = smaller;
                    }
                }

                return null;
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 用 GetPixels 而不是 SKBitmap.Decode，以便把 IncompleteInput/ErrorInInput 等
    /// 损坏图片结果当成失败，而不是返回一张只解了一部分的位图。
    /// </summary>
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

    private static byte[] Encode(SKBitmap bitmap, bool png, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 20, 95));
        return data?.ToArray() ?? [];
    }
}
