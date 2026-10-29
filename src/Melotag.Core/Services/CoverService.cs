namespace Melotag.Core.Services;

using SkiaSharp;

public sealed record CoverResult(byte[] Data, string MimeType, int Width, int Height);

/// <summary>封面校验、缩放与重编码。全部在内存完成，不依赖 Windows 图形栈。</summary>
public sealed class CoverService
{
    public CoverResult? Normalize(byte[] data, int maxWidth, int maxHeight, int maxSizeKb, IReadOnlyCollection<string> allowedFormats)
    {
        if (data.Length == 0) return null;

        var allowed = new HashSet<string>(allowedFormats.Select(f => f.TrimStart('.').ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) allowed = ["jpg", "png"];

        try
        {
            using var encoded = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(encoded);
            if (codec is null) return null;

            var family = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => "jpg",
                SKEncodedImageFormat.Png => "png",
                SKEncodedImageFormat.Bmp => "bmp",
                SKEncodedImageFormat.Gif => "gif",
                SKEncodedImageFormat.Webp => "webp",
                _ => codec.EncodedFormat.ToString().ToLowerInvariant(),
            };
            if (!allowed.Contains(family)) return null;

            var current = SKBitmap.Decode(codec);
            if (current is null || current.Width <= 0 || current.Height <= 0) return null;

            try
            {
                var maxW = Math.Max(64, maxWidth);
                var maxH = Math.Max(64, maxHeight);
                var scale = Math.Min(1.0, Math.Min((double)maxW / current.Width, (double)maxH / current.Height));
                if (scale < 1.0)
                {
                    var scaled = Resize(current, (int)(current.Width * scale), (int)(current.Height * scale));
                    current.Dispose();
                    current = scaled;
                }

                var usePng = family == "png" && allowed.Contains("png");
                var budget = Math.Max(16, maxSizeKb) * 1024;

                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var bytes = Encode(current, usePng, 85 - attempt * 15);
                    if (bytes.Length <= budget || attempt == 3)
                        return new CoverResult(bytes, usePng ? "image/png" : "image/jpeg", current.Width, current.Height);

                    if (current.Width > 64 && current.Height > 64)
                    {
                        var smaller = Resize(current, current.Width * 4 / 5, current.Height * 4 / 5);
                        current.Dispose();
                        current = smaller;
                    }
                }

                return null;
            }
            finally
            {
                current.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? source.Copy();
    }

    private static byte[] Encode(SKBitmap bitmap, bool png, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 20, 95));
        return data?.ToArray() ?? [];
    }
}
