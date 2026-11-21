using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace NetieSpace.Apps.Image;

/// <summary>How a picture is written: the format and its one tunable. The same value drives the file and the page's size figure.</summary>
public sealed record EncodeSpec(ImageKind Kind, int JpegQuality = 90, bool Compressed = false, int Colors = 0);

/// <summary>One finished picture in memory: exactly what a runner writes, so the page can measure it before a run.</summary>
public sealed record OutFile(byte[] Bytes, ImageKind Kind, string Suffix, int Width, int Height, EncodeSpec Spec, string? Note = null);

/// <summary>
/// The image tools as functions of (file, options) -> bytes. <see cref="ImageToolProvider"/> writes the bytes;
/// the page's before/after preview and size figure call the same functions, so what is shown is what a run makes.
/// </summary>
public static class ImageOps
{
    public const int MinJpegQuality = 30;
    private static readonly int[] PaletteSteps = { 256, 128, 64, 32, 16 };

    // ---- encoding --------------------------------------------------------------------------------

    public static EncodeSpec DefaultSpec(ImageKind kind, int jpegQuality = 90) => new(kind, jpegQuality);

    public static byte[] Encode(Image<Rgba32> img, EncodeSpec s)
    {
        using var ms = new MemoryStream();
        switch (s.Kind)
        {
            case ImageKind.Jpeg:
                img.Save(ms, new JpegEncoder { Quality = s.JpegQuality });
                break;
            case ImageKind.Png:
                if (!s.Compressed)
                {
                    img.Save(ms, new PngEncoder());
                    break;
                }
                var png = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                if (s.Colors > 0)
                {
                    png.ColorType = PngColorType.Palette;
                    png.Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = s.Colors });
                }
                img.Save(ms, png);
                break;
            case ImageKind.Gif:
                img.Save(ms, s.Compressed
                    ? new GifEncoder { Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = s.Colors > 0 ? s.Colors : 256 }) }
                    : new GifEncoder());
                break;
            default:
                img.Save(ms, new BmpEncoder());
                break;
        }
        return ms.ToArray();
    }

    /// <summary>The setting a compression level names, before any size target is applied.</summary>
    public static EncodeSpec BaseCompressSpec(ImageKind kind, string level)
    {
        level = level.ToLowerInvariant();
        return kind switch
        {
            ImageKind.Jpeg => new EncodeSpec(kind, level switch { "extreme" => 45, "low" => 88, _ => 72 }, true),
            ImageKind.Png => new EncodeSpec(kind, 0, true, level == "low" ? 0 : level == "extreme" ? 64 : 256),
            _ => new EncodeSpec(kind, 0, true, level switch { "extreme" => 32, "low" => 256, _ => 128 }),
        };
    }

    /// <summary>
    /// Encodes at the level's setting; when <paramref name="target"/> bytes is given and the result is larger, steps the
    /// quality (jpg) or the palette (png, gif) down until it fits. The pixels are never resized: when the target cannot be
    /// met at the lowest setting this throws and says so, rather than shrinking the picture behind the person's back.
    /// </summary>
    public static (byte[] Bytes, EncodeSpec Spec) CompressTo(Image<Rgba32> img, ImageKind kind, string level, long? target, string name)
    {
        var spec = BaseCompressSpec(kind, level);
        var bytes = Encode(img, spec);
        if (target is null || bytes.Length <= target)
            return (bytes, spec);

        byte[]? best = null;
        EncodeSpec? bestSpec = null;
        if (kind == ImageKind.Jpeg)
        {
            int lo = MinJpegQuality, hi = spec.JpegQuality - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var trySpec = spec with { JpegQuality = mid };
                var b = Encode(img, trySpec);
                if (b.Length <= target) { best = b; bestSpec = trySpec; lo = mid + 1; }
                else hi = mid - 1;
            }
        }
        else
        {
            var current = spec.Colors == 0 ? int.MaxValue : spec.Colors;
            foreach (var colors in PaletteSteps)
            {
                if (colors >= current) continue;
                var trySpec = spec with { Colors = colors };
                var b = Encode(img, trySpec);
                bytes = b;
                if (b.Length <= target) { best = b; bestSpec = trySpec; break; }
            }
        }

        if (best is null || bestSpec is null)
            throw new InvalidOperationException(
                $"could not get {name} under {target / 1024} KB at its size ({img.Width} x {img.Height}) without a visible loss; resize it smaller first (the Web 1600 preset does)");
        return (best, bestSpec);
    }

    // ---- per-picture operations (shared by the runner and the preview) -----------------------------

    public static void Resize(Image<Rgba32> img, IReadOnlyDictionary<string, string> o)
    {
        var mode = ImageIo.StrOpt(o, "mode", "pixels");
        var width = ImageIo.IntOpt(o, "width");
        var height = ImageIo.IntOpt(o, "height");
        var cover = string.Equals(ImageIo.StrOpt(o, "fit", "inside"), "cover", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(mode, "pixels", StringComparison.OrdinalIgnoreCase);
        if (cover)
        {
            if (width < 1) throw new ArgumentException("option 'width' is required to fill a frame");
            if (height < 1) throw new ArgumentException("option 'height' is required to fill a frame");
            ImageIo.CheckPixelBudget(width, height, "the resized image");
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Sampler = KnownResamplers.Lanczos3,
            }));
            return;
        }
        var (w, h) = ImageGeometry.ResizeTarget(
            img.Width, img.Height, mode, width, height,
            ImageIo.IntOpt(o, "percent", 50), ImageIo.BoolOpt(o, "keep_ratio", true));
        ImageIo.CheckPixelBudget(w, h, "the resized image");
        img.Mutate(x => x.Resize(w, h, KnownResamplers.Lanczos3));
    }

    /// <summary>The option check that must run before the file is opened.</summary>
    public static void CheckCropOptions(IReadOnlyDictionary<string, string> o)
    {
        if (o.TryGetValue("box", out var box) && !string.IsNullOrWhiteSpace(box))
        {
            if (box.Split(',').Length == 3) ImageGeometry.ParseZoomBox(box); else ImageGeometry.ParseBox(box);
            return;
        }
        if (!o.ContainsKey("width")) throw new ArgumentException("option 'width' is required");
        if (!o.ContainsKey("height")) throw new ArgumentException("option 'height' is required");
    }

    /// <summary>The rectangle in pixels of a picture of this size: from the drawn box, else from x, y, width, height.</summary>
    public static (int X, int Y, int W, int H) CropRectFor(int srcW, int srcH, IReadOnlyDictionary<string, string> o)
    {
        if (o.TryGetValue("box", out var box) && !string.IsNullOrWhiteSpace(box))
        {
            var ratio = ImageGeometry.ParseAspect(ImageIo.StrOpt(o, "aspect", "free"));
            if (box.Split(',').Length == 3)
            {
                var (cx, cy, zoom) = ImageGeometry.ParseZoomBox(box);
                if (ratio is not { } r)
                    throw new ArgumentException("option 'box': a centre and zoom needs a fixed shape in option 'aspect'");
                return ImageGeometry.CropFromZoom(srcW, srcH, cx, cy, zoom, r);
            }
            var (fx, fy, fw, fh) = ImageGeometry.ParseBox(box);
            return ImageGeometry.CropFromBox(srcW, srcH, fx, fy, fw, fh, ratio);
        }
        return (ImageIo.IntOpt(o, "x"), ImageIo.IntOpt(o, "y"), ImageIo.IntOpt(o, "width"), ImageIo.IntOpt(o, "height"));
    }

    public static void Crop(Image<Rgba32> img, IReadOnlyDictionary<string, string> o)
    {
        var (cx, cy, cw, ch) = CropRectFor(img.Width, img.Height, o);
        ImageGeometry.CheckCrop(img.Width, img.Height, cx, cy, cw, ch);
        img.Mutate(x => x.Crop(new Rectangle(cx, cy, cw, ch)));
    }

    public static void Rotate(Image<Rgba32> img, int degrees)
    {
        var mode = degrees switch { 90 => RotateMode.Rotate90, 180 => RotateMode.Rotate180, _ => RotateMode.Rotate270 };
        img.Mutate(x => x.Rotate(mode));
    }

    /// <summary>A watermark checked and ready: everything that can be wrong with the options is found before a file is opened.</summary>
    public sealed class WatermarkPlan
    {
        public string? Text;
        public string? MarkPath;
        public float Opacity;
        public int Scale;
        public string Position = "center";
        public Color TextColor;
        public SixLabors.Fonts.FontFamily Family;
    }

    public static WatermarkPlan PlanWatermark(IReadOnlyDictionary<string, string> o)
    {
        var hasText = o.TryGetValue("text", out var text) && !string.IsNullOrEmpty(text);
        var hasImage = o.TryGetValue("image", out var markPath) && !string.IsNullOrEmpty(markPath);
        if (hasText && hasImage)
            throw new ArgumentException("give option 'text' or option 'image', not both");
        if (!hasText && !hasImage)
            throw new ArgumentException("option 'text' or option 'image' is required (text to stamp, or a png, jpg, gif or bmp)");
        var plan = new WatermarkPlan
        {
            Opacity = ImageIo.IntOpt(o, "opacity", 50) / 100f,
            Scale = ImageIo.IntOpt(o, "scale", 25),
            Position = ImageIo.StrOpt(o, "position", "center"),
        };
        if (hasText)
        {
            plan.Text = ImageText.CleanText("text", text);
            plan.TextColor = ImageIo.ParseOpaqueHex("color", ImageIo.StrOpt(o, "color", "#808080"));
            plan.Family = ImageText.ResolveFamily();
        }
        else
        {
            plan.MarkPath = markPath;
        }
        return plan;
    }

    public static void Watermark(Image<Rgba32> img, WatermarkPlan plan)
    {
        if (plan.MarkPath is not null)
        {
            var (mark, _) = ImageIo.Load(plan.MarkPath);
            using (mark)
            {
                mark.Mutate(x => x.AutoOrient());
                var (mw, mh) = ImageGeometry.OverlaySize(img.Width, img.Height, mark.Width, mark.Height, plan.Scale);
                mark.Mutate(x => x.Resize(mw, mh, KnownResamplers.Lanczos3));
                var (px, py) = ImageGeometry.OverlayOrigin(img.Width, img.Height, mw, mh, plan.Position);
                img.Mutate(x => x.DrawImage(mark, new Point(px, py), plan.Opacity));
            }
            return;
        }

        // Pick the size so the widest line spans scale% of the image width, then shrink to fit the height.
        var text = plan.Text!;
        var target = Math.Max(1.0, img.Width * plan.Scale / 100.0);
        var probe = ImageText.Layout(text, plan.Family, 100f, true, 0);
        var size = (float)Math.Max(4.0, 100.0 * target / Math.Max(1f, probe.Width));
        var ink = ImageText.Layout(text, plan.Family, size, true, 0);
        if (ink.Height > img.Height * 0.9f)
        {
            size = Math.Max(4f, size * img.Height * 0.9f / ink.Height);
            ink = ImageText.Layout(text, plan.Family, size, true, 0);
        }
        var (tx, ty) = ImageGeometry.OverlayOrigin(img.Width, img.Height,
            Math.Min(img.Width, (int)Math.Ceiling(ink.Width)), Math.Min(img.Height, (int)Math.Ceiling(ink.Height)), plan.Position);
        ImageText.Draw(img, ink, tx, ty, plan.TextColor.ToPixel<Rgba32>(), plan.Opacity);
    }

    /// <summary>Flattens onto an opaque colour so a jpg has no alpha.</summary>
    public static Image<Rgba32> FirstFrame(Image<Rgba32> img) =>
        img.Frames.Count > 1 ? img.Frames.CloneFrame(0) : img.Clone();

    // ---- whole-file runs: the same bytes the runners write ----------------------------------------

    private static OutFile Finish(Image<Rgba32> img, ImageKind kind, string suffix, EncodeSpec spec, string? note = null) =>
        new(Encode(img, spec), kind, suffix, img.Width, img.Height, spec, note);

    public static OutFile ResizeFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            Resize(img, o);
            return Finish(img, kind, "-resized", DefaultSpec(kind));
        }
    }

    public static OutFile CropFile(string input, IReadOnlyDictionary<string, string> o)
    {
        CheckCropOptions(o);
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            Crop(img, o);
            return Finish(img, kind, "-cropped", DefaultSpec(kind));
        }
    }

    public static OutFile RotateFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var degrees = ImageGeometry.RotationDegrees(ImageIo.StrOpt(o, "direction", "right"));
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            // AutoOrient bakes any EXIF orientation into the pixels and resets the tag, so the
            // rotation is relative to what a viewer showed.
            img.Mutate(x => x.AutoOrient());
            Rotate(img, degrees);
            return Finish(img, kind, "-rotated", DefaultSpec(kind));
        }
    }

    public static OutFile WatermarkFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var plan = PlanWatermark(o);
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            Watermark(img, plan);
            return Finish(img, kind, "-watermarked", DefaultSpec(kind));
        }
    }

    /// <summary>The target of a compression in bytes, or null when none was asked for.</summary>
    public static long? TargetBytes(IReadOnlyDictionary<string, string> o)
    {
        var kb = ImageIo.IntOpt(o, "target_kb", 0);
        if (o.TryGetValue("target_kb", out var raw) && !string.IsNullOrWhiteSpace(raw) && kb < 1)
            throw new ArgumentException("option 'target_kb' must be at least 1");
        return kb >= 1 ? kb * 1024L : null;
    }

    public static OutFile CompressFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var level = ImageIo.StrOpt(o, "level", "recommended").ToLowerInvariant();
        var target = TargetBytes(o);
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            if (kind == ImageKind.Bmp)
                throw new NotSupportedException("bmp has no compression to apply; convert it to png or jpg first: " + input);
            img.Mutate(x => x.AutoOrient());
            img.Metadata.ExifProfile = null;
            var (bytes, spec) = CompressTo(img, kind, level, target, input);
            var before = new FileInfo(input).Length;
            if (bytes.Length >= before)
            {
                if (target is not null && before <= target)
                    return new OutFile(File.ReadAllBytes(input), kind, "-compressed", img.Width, img.Height, spec,
                        "already under the target; copied unchanged");
                throw new InvalidOperationException(
                    $"could not make {input} smaller at level '{level}' ({before} bytes in, {bytes.Length} bytes out); it is already compact");
            }
            return new OutFile(bytes, kind, "-compressed", img.Width, img.Height, spec);
        }
    }

    public static OutFile ToJpgFile(string input, string suffix, IReadOnlyDictionary<string, string> o, bool refuseJpg)
    {
        var quality = ImageIo.IntOpt(o, "quality", 90);
        var bg = ImageIo.ParseOpaqueHex("background", ImageIo.StrOpt(o, "background", "#FFFFFF"));
        var (src, srcKind) = ImageIo.Load(input);
        using (src)
        using (var flat = FirstFrame(src))
        {
            if (refuseJpg && srcKind == ImageKind.Jpeg)
                throw new InvalidOperationException("input is already jpg; pick a different option 'to': " + input);
            flat.Mutate(x => x.AutoOrient());
            flat.Mutate(x => x.BackgroundColor(bg));
            return Finish(flat, ImageKind.Jpeg, suffix, DefaultSpec(ImageKind.Jpeg, quality));
        }
    }

    /// <summary>Re-encodes the first frame as <paramref name="target"/>. <paramref name="requireJpg"/> is the from-jpg tool's check.</summary>
    public static OutFile ConvertFile(string input, ImageKind target, string suffix, bool refuseSame, bool requireJpg)
    {
        var (src, kind) = ImageIo.Load(input);
        using (src)
        {
            if (requireJpg && kind != ImageKind.Jpeg)
                throw new InvalidOperationException("input is not a JPG (it is " + kind.ToString().ToLowerInvariant() + "): " + input);
            if (refuseSame && kind == target)
                throw new InvalidOperationException(
                    $"input is already {target.ToString().ToLowerInvariant()}; pick a different option 'to': {input}");
            src.Mutate(x => x.AutoOrient());
            using var frame = FirstFrame(src);
            return Finish(frame, target, suffix, DefaultSpec(target));
        }
    }

    public static OutFile ConvertAnyFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var target = ImageIo.KindFromName(ImageIo.StrOpt(o, "to", "png"));
        return target == ImageKind.Jpeg
            ? ToJpgFile(input, "-converted", o, true)
            : ConvertFile(input, target, "-converted", true, false);
    }

    public static OutFile FromJpgFile(string input, IReadOnlyDictionary<string, string> o)
    {
        var target = ImageIo.KindFromName(ImageIo.StrOpt(o, "format", "png"));
        return ConvertFile(input, target, "-" + target.ToString().ToLowerInvariant(), false, true);
    }
}
