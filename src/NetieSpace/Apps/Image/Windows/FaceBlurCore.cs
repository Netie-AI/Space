using System;
using System.Collections.Generic;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SRect = SixLabors.ImageSharp.Rectangle;

namespace NetieSpace.Apps.Image.WinRt;

/// <summary>
/// The pixel half of img-blur-face: parsing rectangles and blurring or pixelating them. Pure ImageSharp,
/// no WinRT, so it can be exercised anywhere. Lives under Windows\ only because its one caller does.
/// </summary>
public static class FaceBlurCore
{
    /// <summary>
    /// Parses "x,y,w,h;x,y,w,h" (pixels, origin top-left). Each rectangle is clipped to the image. Throws
    /// naming option 'regions' and the offending rectangle for bad text, a non-positive size, or a
    /// rectangle that lies wholly outside the image.
    /// </summary>
    public static IReadOnlyList<SRect> ParseRegions(string text, int imageWidth, int imageHeight)
    {
        var result = new List<SRect>();
        var parts = text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException("option 'regions': no rectangle given; use x,y,w,h;x,y,w,h");
        for (var i = 0; i < parts.Length; i++)
        {
            var label = "rectangle " + (i + 1) + " ('" + parts[i].Trim() + "')";
            var f = parts[i].Split(',');
            if (f.Length != 4)
                throw new ArgumentException("option 'regions': " + label + " needs four numbers x,y,w,h");
            var n = new int[4];
            for (var k = 0; k < 4; k++)
                if (!int.TryParse(f[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n[k]))
                    throw new ArgumentException("option 'regions': " + label + " has '" + f[k].Trim() + "', which is not a whole number");
            if (n[2] <= 0 || n[3] <= 0)
                throw new ArgumentException("option 'regions': " + label + " needs a width and height above zero");
            long x0 = Math.Max(0L, n[0]), y0 = Math.Max(0L, n[1]);
            long x1 = Math.Min((long)imageWidth, (long)n[0] + n[2]);
            long y1 = Math.Min((long)imageHeight, (long)n[1] + n[3]);
            if (x1 <= x0 || y1 <= y0)
                throw new ArgumentException("option 'regions': " + label + " lies outside the " + imageWidth + "x" + imageHeight + " image");
            result.Add(new SRect((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0)));
        }
        return result;
    }

    /// <summary>Grows a box by a fraction of its size on every side and clips it to the image.</summary>
    public static SRect Pad(SRect box, double fraction, int imageWidth, int imageHeight)
    {
        var dx = (int)Math.Round(box.Width * fraction);
        var dy = (int)Math.Round(box.Height * fraction);
        var x0 = Math.Max(0, box.X - dx);
        var y0 = Math.Max(0, box.Y - dy);
        var x1 = Math.Min(imageWidth, box.Right + dx);
        var y1 = Math.Min(imageHeight, box.Bottom + dy);
        return new SRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>Gaussian sigma for a box at a strength of 1..100.</summary>
    public static float SigmaFor(SRect box, int strength) =>
        Math.Max(2f, strength / 100f * Math.Max(box.Width, box.Height) / 3f);

    /// <summary>Pixelation block size for a box at a strength of 1..100.</summary>
    public static int BlockFor(SRect box, int strength) =>
        Math.Max(4, (int)(Math.Min(box.Width, box.Height) * (strength / 100.0) / 4.0));

    /// <summary>Blurs or pixelates every box in place; pixels outside the boxes are not written.</summary>
    public static void Apply(SixLabors.ImageSharp.Image<Rgba32> image, IEnumerable<SRect> boxes, string mode, int strength)
    {
        if (strength < 1 || strength > 100)
            throw new ArgumentException("option 'strength': " + strength + " is outside 1..100");
        var pixelate = mode switch
        {
            "pixelate" => true,
            "blur" => false,
            _ => throw new ArgumentException("option 'mode': '" + mode + "' is not blur or pixelate"),
        };
        foreach (var box in boxes)
        {
            if (box.Width <= 0 || box.Height <= 0) continue;
            var b = SRect.Intersect(box, new SRect(0, 0, image.Width, image.Height));
            if (b.Width <= 0 || b.Height <= 0) continue;
            if (pixelate)
                image.Mutate(c => c.Pixelate(BlockFor(b, strength), b));
            else
                image.Mutate(c => c.GaussianBlur(SigmaFor(b, strength), b));
        }
    }
}
