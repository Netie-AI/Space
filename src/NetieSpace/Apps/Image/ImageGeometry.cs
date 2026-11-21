using System;

namespace NetieSpace.Apps.Image;

/// <summary>Pure size and placement maths for the image tools. No imaging library in here.</summary>
public static class ImageGeometry
{
    public const int MaxSide = 30000;
    public const long MaxPixels = 100_000_000;

    /// <summary>
    /// Target size for a resize. <paramref name="mode"/> is "pixels" or "percentage". With pixels and
    /// keep-ratio, one side may be omitted (0) and the other is derived; if both are given the result is
    /// the largest size that fits inside the box. Without keep-ratio both sides are required.
    /// </summary>
    public static (int W, int H) ResizeTarget(int srcW, int srcH, string mode, int width, int height, int percent, bool keepRatio)
    {
        if (srcW < 1 || srcH < 1) throw new ArgumentException("source image has no pixels");
        int w, h;
        if (string.Equals(mode, "percentage", StringComparison.OrdinalIgnoreCase))
        {
            if (percent < 1) throw new ArgumentException("option 'percent' must be at least 1");
            w = Scale(srcW, percent);
            h = Scale(srcH, percent);
        }
        else
        {
            if (width < 1 && height < 1)
                throw new ArgumentException("option 'width' or 'height' is required in pixels mode");
            if (!keepRatio)
            {
                if (width < 1) throw new ArgumentException("option 'width' is required when keep_ratio is false");
                if (height < 1) throw new ArgumentException("option 'height' is required when keep_ratio is false");
                w = width; h = height;
            }
            else if (width < 1)
            {
                h = height; w = Math.Max(1, (int)Math.Round((double)srcW * height / srcH));
            }
            else if (height < 1)
            {
                w = width; h = Math.Max(1, (int)Math.Round((double)srcH * width / srcW));
            }
            else
            {
                var f = Math.Min((double)width / srcW, (double)height / srcH);
                w = Math.Max(1, (int)Math.Round(srcW * f));
                h = Math.Max(1, (int)Math.Round(srcH * f));
            }
        }
        if (w > MaxSide || h > MaxSide || (long)w * h > MaxPixels)
            throw new ArgumentException($"resulting size {w}x{h} is larger than the limit ({MaxSide} per side, {MaxPixels / 1_000_000} megapixels)");
        return (w, h);
    }

    private static int Scale(int v, int percent) => Math.Max(1, (int)Math.Round(v * (double)percent / 100.0));

    /// <summary>Validates a crop rectangle against the image; the message names the option at fault.</summary>
    public static void CheckCrop(int srcW, int srcH, int x, int y, int w, int h)
    {
        if (x < 0) throw new ArgumentException("option 'x' must be 0 or more");
        if (y < 0) throw new ArgumentException("option 'y' must be 0 or more");
        if (w < 1) throw new ArgumentException("option 'width' must be at least 1");
        if (h < 1) throw new ArgumentException("option 'height' must be at least 1");
        if (x >= srcW) throw new ArgumentException($"option 'x' ({x}) is outside the image width {srcW}");
        if (y >= srcH) throw new ArgumentException($"option 'y' ({y}) is outside the image height {srcH}");
        if ((long)x + w > srcW) throw new ArgumentException($"crop reaches x={(long)x + w} but the image is {srcW} wide (option 'width')");
        if ((long)y + h > srcH) throw new ArgumentException($"crop reaches y={(long)y + h} but the image is {srcH} high (option 'height')");
    }

    /// <summary>The ratio width / height of a shape choice ("1:1", "4:5", "16:9"); null for "free" or nothing.</summary>
    public static double? ParseAspect(string? aspect)
    {
        if (string.IsNullOrWhiteSpace(aspect) || string.Equals(aspect.Trim(), "free", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = aspect.Trim().Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)
            || a <= 0 || b <= 0)
            throw new ArgumentException($"option 'aspect': '{aspect}' is not free, or a ratio like 1:1");
        return a / b;
    }

    /// <summary>Reads "fx,fy,fw,fh": a crop box as fractions (0 to 1) of the picture, left, top, width, height.</summary>
    public static (double X, double Y, double W, double H) ParseBox(string box)
    {
        var parts = box.Split(',');
        var v = new double[4];
        if (parts.Length != 4)
            throw new ArgumentException($"option 'box': '{box}' must be four numbers: left,top,width,height as fractions of the picture");
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])
                || double.IsNaN(v[i]) || double.IsInfinity(v[i]))
                throw new ArgumentException($"option 'box': '{parts[i].Trim()}' is not a number");
        if (v[0] < 0 || v[1] < 0 || v[2] <= 0 || v[3] <= 0 || v[0] + v[2] > 1.0001 || v[1] + v[3] > 1.0001)
            throw new ArgumentException($"option 'box': '{box}' must lie inside the picture (every number from 0 to 1)");
        return (v[0], v[1], v[2], v[3]);
    }

    /// <summary>
    /// A drawn box (fractions of the picture) as a pixel rectangle on a picture of this size. With a fixed shape the box is
    /// re-fitted to it: the same width share, the same centre, kept inside the picture, so one box drawn on the first photo
    /// gives every other photo the same shape (a square stays a square on a portrait and on a landscape).
    /// </summary>
    public static (int X, int Y, int W, int H) CropFromBox(int srcW, int srcH, double fx, double fy, double fw, double fh, double? ratio)
    {
        if (srcW < 1 || srcH < 1) throw new ArgumentException("source image has no pixels");
        double w = fw * srcW, h = fh * srcH;
        var cx = (fx + fw / 2) * srcW;
        var cy = (fy + fh / 2) * srcH;
        if (ratio is { } r)
        {
            h = w / r;
            if (h > srcH) { h = srcH; w = h * r; }
            if (w > srcW) { w = srcW; h = w / r; }
        }
        var pw = (int)Math.Clamp(Math.Round(w), 1, srcW);
        var ph = (int)Math.Clamp(Math.Round(ratio is { } rr ? pw / rr : h), 1, srcH);
        if (ratio is { } r2 && ph == srcH && Math.Abs((double)pw / ph - r2) > 0.5 / ph)
            pw = (int)Math.Clamp(Math.Round(ph * r2), 1, srcW);
        var px = (int)Math.Clamp(Math.Round(cx - pw / 2.0), 0, srcW - pw);
        var py = (int)Math.Clamp(Math.Round(cy - ph / 2.0), 0, srcH - ph);
        return (px, py, pw, ph);
    }

    /// <summary>
    /// Reads "cx,cy,zoom" for a fixed shape: the box centre as fractions of the picture, and how much of the largest box of
    /// that shape it is (1 is the largest). The same three numbers fit any photo, whatever its proportions.
    /// </summary>
    public static (double Cx, double Cy, double Zoom) ParseZoomBox(string box)
    {
        var parts = box.Split(',');
        var v = new double[3];
        if (parts.Length != 3)
            throw new ArgumentException($"option 'box': '{box}' must be three numbers: centre x, centre y, zoom");
        for (var i = 0; i < 3; i++)
            if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])
                || double.IsNaN(v[i]) || double.IsInfinity(v[i]))
                throw new ArgumentException($"option 'box': '{parts[i].Trim()}' is not a number");
        if (v[0] < 0 || v[0] > 1 || v[1] < 0 || v[1] > 1 || v[2] <= 0 || v[2] > 1.0001)
            throw new ArgumentException($"option 'box': '{box}' needs a centre inside the picture (0 to 1) and a zoom above 0 up to 1");
        return (v[0], v[1], Math.Min(1.0, v[2]));
    }

    /// <summary>The pixel rectangle of a fixed-shape box given as a centre and a zoom of the largest box of that shape.</summary>
    public static (int X, int Y, int W, int H) CropFromZoom(int srcW, int srcH, double cx, double cy, double zoom, double ratio)
    {
        if (srcW < 1 || srcH < 1) throw new ArgumentException("source image has no pixels");
        var largestW = Math.Min((double)srcW, srcH * ratio);
        var pw = (int)Math.Clamp(Math.Round(largestW * zoom), 1, srcW);
        var ph = (int)Math.Clamp(Math.Round(pw / ratio), 1, srcH);
        if (ph == srcH && Math.Abs((double)pw / ph - ratio) > 0.5 / ph)
            pw = (int)Math.Clamp(Math.Round(ph * ratio), 1, srcW);
        var px = (int)Math.Clamp(Math.Round(cx * srcW - pw / 2.0), 0, srcW - pw);
        var py = (int)Math.Clamp(Math.Round(cy * srcH - ph / 2.0), 0, srcH - ph);
        return (px, py, pw, ph);
    }

    /// <summary>Rotation in degrees clockwise for a direction choice.</summary>
    public static int RotationDegrees(string direction) => direction.ToLowerInvariant() switch
    {
        "right" => 90,
        "left" => 270,
        "180" => 180,
        _ => throw new ArgumentException("option 'direction' must be left, right or 180"),
    };

    /// <summary>Where a pixel at (x, y) of a W x H image lands after a clockwise rotation, and the new size.</summary>
    public static (int X, int Y, int W, int H) RotatePoint(int w, int h, int x, int y, int degrees) => degrees switch
    {
        90 => (h - 1 - y, x, h, w),
        180 => (w - 1 - x, h - 1 - y, w, h),
        270 => (y, w - 1 - x, h, w),
        _ => (x, y, w, h),
    };

    /// <summary>Overlay size: a percent of the base width, kept inside the base and never below 1 pixel.</summary>
    public static (int W, int H) OverlaySize(int baseW, int baseH, int ovW, int ovH, int scalePercent)
    {
        var w = Math.Max(1, (int)Math.Round(baseW * (double)scalePercent / 100.0));
        var h = Math.Max(1, (int)Math.Round((double)ovH * w / ovW));
        if (h > baseH)
        {
            h = baseH;
            w = Math.Max(1, (int)Math.Round((double)ovW * h / ovH));
        }
        return (Math.Min(w, baseW), Math.Min(h, baseH));
    }

    /// <summary>Top-left corner for an overlay at a grid position, inset by a margin of 2% of the short side.</summary>
    public static (int X, int Y) OverlayOrigin(int baseW, int baseH, int ovW, int ovH, string position)
    {
        var p = position.Trim().ToLowerInvariant();
        string v, hz;
        switch (p)
        {
            case "center": v = "middle"; hz = "center"; break;
            case "north": v = "top"; hz = "center"; break;
            case "south": v = "bottom"; hz = "center"; break;
            case "east": v = "middle"; hz = "right"; break;
            case "west": v = "middle"; hz = "left"; break;
            case "northeast": v = "top"; hz = "right"; break;
            case "northwest": v = "top"; hz = "left"; break;
            case "southeast": v = "bottom"; hz = "right"; break;
            case "southwest": v = "bottom"; hz = "left"; break;
            default:
            {
                var parts = p.Split('-');
                if (parts.Length != 2) throw new ArgumentException($"option 'position': '{position}' is not a grid position");
                v = parts[0]; hz = parts[1];
                break;
            }
        }
        var margin = (int)Math.Round(Math.Min(baseW, baseH) * 0.02);
        int x = hz switch
        {
            "left" => margin,
            "center" => (baseW - ovW) / 2,
            "right" => baseW - ovW - margin,
            _ => throw new ArgumentException($"option 'position': '{position}' is not a grid position"),
        };
        int y = v switch
        {
            "top" => margin,
            "middle" => (baseH - ovH) / 2,
            "bottom" => baseH - ovH - margin,
            _ => throw new ArgumentException($"option 'position': '{position}' is not a grid position"),
        };
        return (Math.Max(0, x), Math.Max(0, y));
    }
}
