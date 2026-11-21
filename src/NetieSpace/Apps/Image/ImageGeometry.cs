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
