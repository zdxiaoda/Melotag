namespace Melotag.Branding;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// 生成 Melotag 品牌资源。图形按归一化坐标（0..1）用有向距离场描边，
/// 所以同一份几何可以无损地输出 16px 到 1024px。
/// </summary>
internal static class Program
{
    private const double Gx0 = 0x6E, Gy0 = 0x5B, Gz0 = 0xF5; // #6E5BF5
    private const double Gx1 = 0x18, Gy1 = 0xBE, Gz1 = 0xD6; // #18BED6

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "emit";
        var target = args.Length > 1 ? args[1] : ".";

        switch (mode)
        {
            case "sheet":
                RenderSheet(target);
                break;
            case "emit":
                Emit(target);
                break;
            default:
                Console.Error.WriteLine("用法: Melotag.Branding [emit|sheet] <output-dir>");
                return 1;
        }

        return 0;
    }

    // ---------- 图形定义 ----------

    private enum Variant
    {
        Bars,
        BarsWithHole,
        WaveWithHole,
    }

    private readonly record struct Shape(double R, double G, double B, double A);

    private static Shape RenderPixel(double x, double y, int size, Variant variant)
    {
        var px = 1.0 / size;

        // 圆角方块底板
        var tile = Coverage(RoundRect(x, y, 0.5, 0.5, 0.478, 0.478, 0.215), size);

        // 标签孔：直接打穿底板（像行李牌的穿孔）
        if (variant != Variant.Bars)
            tile *= 1 - Coverage(Circle(x, y, 0.226, 0.232, 0.058), size);

        if (tile <= 0) return default;

        var t = Math.Clamp((x * 0.45 + y * 0.55 - 0.09) / 0.82, 0, 1);
        var result = new Shape(
            (Gx0 + (Gx1 - Gx0) * t) / 255,
            (Gy0 + (Gy1 - Gy0) * t) / 255,
            (Gz0 + (Gz1 - Gz0) * t) / 255,
            tile);

        double mark;
        if (variant == Variant.WaveWithHole)
        {
            mark = Waveform(x, y);
        }
        else
        {
            mark = Math.Max(
                Math.Max(
                    Coverage(RoundRect(x, y, 0.354, 0.575, 0.054, 0.165, 0.054), size),
                    Coverage(RoundRect(x, y, 0.525, 0.575, 0.054, 0.275, 0.054), size)),
                Coverage(RoundRect(x, y, 0.696, 0.575, 0.054, 0.208, 0.054), size));
        }

        if (mark > 0)
            result = Over(new Shape(1, 1, 1, mark), result);

        _ = px;
        return result;
    }

    private static double Waveform(double x, double y)
    {
        double[] xs = [0.238, 0.322, 0.386, 0.452, 0.518, 0.588, 0.654, 0.739];
        double[] ys = [0.575, 0.575, 0.452, 0.668, 0.408, 0.622, 0.5, 0.5];
        var best = double.MaxValue;
        for (var i = 0; i < xs.Length - 1; i++)
            best = Math.Min(best, Segment(x, y, xs[i], ys[i], xs[i + 1], ys[i + 1]));
        return best <= 0.036 ? 1 : 0;
    }

    private static double Segment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 <= 1e-12 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        var cx = ax + dx * t;
        var cy = ay + dy * t;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - 0.036;
    }

    /// <summary>有向距离场 → 覆盖度，aa 为每像素的柔化宽度（单位：像素）。</summary>
    private static double Coverage(double sdf, int size) =>
        Math.Clamp(0.5 - sdf * size, 0, 1);

    private static double Circle(double x, double y, double cx, double cy, double r)
    {
        var dx = x - cx;
        var dy = y - cy;
        return Math.Sqrt(dx * dx + dy * dy) - r;
    }

    private static double RoundRect(double px, double py, double cx, double cy, double hw, double hh, double r)
    {
        var qx = Math.Abs(px - cx) - (hw - r);
        var qy = Math.Abs(py - cy) - (hh - r);
        var ax = Math.Max(qx, 0);
        var ay = Math.Max(qy, 0);
        return Math.Sqrt(ax * ax + ay * ay) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    private static Shape Over(Shape src, Shape dst)
    {
        var a = src.A + dst.A * (1 - src.A);
        if (a <= 0) return default;
        return new Shape(
            (src.R * src.A + dst.R * dst.A * (1 - src.A)) / a,
            (src.G * src.A + dst.G * dst.A * (1 - src.A)) / a,
            (src.B * src.A + dst.B * dst.A * (1 - src.A)) / a,
            a);
    }

    // ---------- 渲染 ----------

    private static Image<Rgba32> Render(int size, Variant variant)
    {
        var image = new Image<Rgba32>(size, size);
        const int samples = 3;
        var offsets = new double[samples];

        for (var i = 0; i < samples; i++)
            offsets[i] = (i + 0.5) / samples;

        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                foreach (var oy in offsets)
                {
                    foreach (var ox in offsets)
                    {
                        var s = RenderPixel((px + ox) / size, (py + oy) / size, size, variant);
                        r += s.R * s.A;
                        g += s.G * s.A;
                        b += s.B * s.A;
                        a += s.A;
                    }
                }

                var n = samples * samples;
                var alpha = a / n;
                var color = alpha <= 0
                    ? new Rgba32(0, 0, 0, 0)
                    : new Rgba32((byte)(r / a * 255), (byte)(g / a * 255), (byte)(b / a * 255), (byte)(alpha * 255));
                image[px, py] = color;
            }
        }

        return image;
    }

    private static void RenderSheet(string directory)
    {
        Directory.CreateDirectory(directory);
        var variants = new[]
        {
            (Variant.Bars, "a-bars"),
            (Variant.BarsWithHole, "b-hole"),
            (Variant.WaveWithHole, "c-wave"),
        };

        const int cell = 240;
        const int gap = 28;
        const int small = 32;
        var width = gap + variants.Length * (cell + gap);
        var height = gap + cell + 24 + small + 24 + 16 + gap;

        using var sheet = new Image<Rgba32>(width, height, new Rgba32(246, 246, 248));

        for (var i = 0; i < variants.Length; i++)
        {
            var x = gap + i * (cell + gap);
            using (var big = Render(cell, variants[i].Item1))
                Blit(sheet, big, x, gap);
            using (var smallImage = Render(small, variants[i].Item1))
                Blit(sheet, smallImage, x, gap + cell + 24);
            using (var tiny = Render(16, variants[i].Item1))
                Blit(sheet, tiny, x + small + 12, gap + cell + 24 + 8);
        }

        var path = Path.Combine(directory, "variants.png");
        sheet.SaveAsPng(path);
        Console.WriteLine(path);
    }

    private static void Blit(Image<Rgba32> canvas, Image<Rgba32> sprite, int ox, int oy)
    {
        for (var y = 0; y < sprite.Height; y++)
        {
            for (var x = 0; x < sprite.Width; x++)
            {
                var s = sprite[x, y];
                if (s.A == 0) continue;
                var d = canvas[ox + x, oy + y];
                var a = s.A / 255.0 + d.A / 255.0 * (1 - s.A / 255.0);
                if (a <= 0) continue;
                var r = (s.R * s.A / 255.0 + d.R * d.A / 255.0 * (1 - s.A / 255.0)) / a;
                var g = (s.G * s.A / 255.0 + d.G * d.A / 255.0 * (1 - s.A / 255.0)) / a;
                var b = (s.B * s.A / 255.0 + d.B * d.A / 255.0 * (1 - s.A / 255.0)) / a;
                canvas[ox + x, oy + y] = new Rgba32((byte)r, (byte)g, (byte)b, (byte)(a * 255));
            }
        }
    }

    // ---------- 输出 ----------

    private static void Emit(string root)
    {
        const Variant variant = Variant.BarsWithHole;
        var uiAssets = Path.Combine(root, "src", "Melotag.UI", "Assets");
        var docs = Path.Combine(root, "docs", "branding");
        Directory.CreateDirectory(uiAssets);
        Directory.CreateDirectory(docs);

        using (var logo = Render(512, variant))
            logo.SaveAsPng(Path.Combine(docs, "melotag-logo.png"));

        using (var logo = Render(256, variant))
            logo.SaveAsPng(Path.Combine(uiAssets, "melotag-logo.png"));

        int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
        var frames = new List<(int Size, byte[] Png)>();
        foreach (var size in icoSizes)
        {
            using var image = Render(size, variant);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            frames.Add((size, stream.ToArray()));
        }

        File.WriteAllBytes(Path.Combine(uiAssets, "Melotag.ico"), BuildIco(frames));
        Console.WriteLine($"logo + ico written to {uiAssets}");
        Console.WriteLine($"pinup written to {docs}");
    }

    private static byte[] BuildIco(IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)frames.Count);   // image count

        var offset = 6 + frames.Count * 16;
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // width
            writer.Write((byte)(size >= 256 ? 0 : size)); // height
            writer.Write((byte)0);            // palette
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // color planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
            writer.Write(png);

        writer.Flush();
        return output.ToArray();
    }
}
