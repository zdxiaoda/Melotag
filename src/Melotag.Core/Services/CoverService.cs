namespace Melotag.Core.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

public sealed record CoverResult(byte[] Data, string MimeType, int Width, int Height);

/// <summary>封面校验、缩放与重编码。所有处理都在内存完成，不依赖 Windows 图形栈。</summary>
public sealed class CoverService
{
    private static readonly HashSet<string> JpegNames = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg" };

    public CoverResult? Normalize(byte[] data, int maxWidth, int maxHeight, int maxSizeKb, IReadOnlyCollection<string> allowedFormats)
    {
        if (data.Length == 0) return null;

        var allowed = new HashSet<string>(allowedFormats.Select(f => f.TrimStart('.').ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) allowed = ["jpg", "png"];

        try
        {
            using var image = Image.Load(data);
            var formatName = image.Metadata.DecodedImageFormat?.Name?.ToLowerInvariant() ?? "jpeg";
            var family = formatName switch
            {
                "jpeg" => "jpg",
                "png" => "png",
                "bmp" => "bmp",
                "gif" => "gif",
                _ => formatName,
            };
            if (!allowed.Contains(family)) return null;

            var maxW = Math.Max(64, maxWidth);
            var maxH = Math.Max(64, maxHeight);
            var scale = Math.Min(1.0, Math.Min((double)maxW / image.Width, (double)maxH / image.Height));
            if (scale < 1.0)
            {
                var w = Math.Max(1, (int)(image.Width * scale));
                var h = Math.Max(1, (int)(image.Height * scale));
                image.Mutate(x => x.Resize(w, h));
            }

            var usePng = family == "png" && allowed.Contains("png");
            var budget = Math.Max(16, maxSizeKb) * 1024;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var bytes = Encode(image, usePng, 85 - attempt * 15);
                if (bytes.Length <= budget || attempt == 3)
                    return new CoverResult(bytes, usePng ? "image/png" : "image/jpeg", image.Width, image.Height);

                if (image.Width > 64 && image.Height > 64)
                    image.Mutate(x => x.Resize(image.Width * 4 / 5, image.Height * 4 / 5));
            }

            return null;
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static byte[] Encode(Image image, bool png, int quality)
    {
        using var ms = new MemoryStream();
        if (png) image.SaveAsPng(ms, new PngEncoder());
        else image.SaveAsJpeg(ms, new JpegEncoder { Quality = Math.Clamp(quality, 20, 95) });
        return ms.ToArray();
    }
}
