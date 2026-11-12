using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace NetieSpace.Apps.Image;

/// <summary>
/// Image tool runners on SixLabors.ImageSharp 1.0.4 (Apache-2.0, in-process, arrives through PdfSharpCore).
/// Formats built: png, jpg, gif, bmp. Everything else throws a "not built" message naming the file.
/// </summary>
public sealed class ImageToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Image;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("img-resize", (r, p, ct) => Batch(r, p, ct, ResizeOne));
        yield return ("img-crop", (r, p, ct) => Batch(r, p, ct, CropOne));
        yield return ("img-rotate", (r, p, ct) => Batch(r, p, ct, RotateOne));
        yield return ("img-compress", (r, p, ct) => Batch(r, p, ct, CompressOne));
        yield return ("img-to-jpg", (r, p, ct) => Batch(r, p, ct, ToJpgOne));
        yield return ("img-from-jpg", (r, p, ct) => Batch(r, p, ct, FromJpgOne));
        yield return ("img-convert-any", (r, p, ct) => Batch(r, p, ct, ConvertAnyOne));
        yield return ("img-watermark", (r, p, ct) => Batch(r, p, ct, WatermarkOne));
        yield return ("img-dpi", (r, p, ct) => Batch(r, p, ct, DpiOne));
    }

    private delegate string OneFile(string input, ToolRequest request, ImageIo.OutputNamer namer);

    private static Task<IReadOnlyList<string>> Batch(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct, OneFile one)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var namer = new ImageIo.OutputNamer(request.OutDir);
            var outputs = new List<string>();
            var total = request.Files.Count;
            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                outputs.Add(one(request.Files[i], request, namer));
                progress?.Report(new ToolProgress((i + 1) * 100 / Math.Max(1, total),
                    "image " + (i + 1) + " of " + total));
            }
            return outputs;
        }, ct);
    }

    private static string Save(Image<Rgba32> img, ImageKind kind, string path, int jpegQuality = 90)
    {
        img.Save(path, ImageIo.Encoder(kind, jpegQuality));
        return path;
    }

    // ---- resize ----------------------------------------------------------------------------------

    private static string ResizeOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var o = rq.Options;
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            var (w, h) = ImageGeometry.ResizeTarget(
                img.Width, img.Height,
                ImageIo.StrOpt(o, "mode", "pixels"),
                ImageIo.IntOpt(o, "width"), ImageIo.IntOpt(o, "height"),
                ImageIo.IntOpt(o, "percent", 50),
                ImageIo.BoolOpt(o, "keep_ratio", true));
            img.Mutate(x => x.Resize(w, h, KnownResamplers.Lanczos3));
            return Save(img, kind, namer.Reserve(input, "-resized", ImageIo.Extension(kind)));
        }
    }

    // ---- crop ------------------------------------------------------------------------------------

    private static string CropOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var o = rq.Options;
        if (!o.ContainsKey("width")) throw new ArgumentException("option 'width' is required");
        if (!o.ContainsKey("height")) throw new ArgumentException("option 'height' is required");
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            var cx = ImageIo.IntOpt(o, "x");
            var cy = ImageIo.IntOpt(o, "y");
            var cw = ImageIo.IntOpt(o, "width");
            var ch = ImageIo.IntOpt(o, "height");
            ImageGeometry.CheckCrop(img.Width, img.Height, cx, cy, cw, ch);
            img.Mutate(x => x.Crop(new Rectangle(cx, cy, cw, ch)));
            return Save(img, kind, namer.Reserve(input, "-cropped", ImageIo.Extension(kind)));
        }
    }

    // ---- rotate ----------------------------------------------------------------------------------

    private static string RotateOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var degrees = ImageGeometry.RotationDegrees(ImageIo.StrOpt(rq.Options, "direction", "right"));
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            // AutoOrient bakes any EXIF orientation into the pixels and resets the tag, so the
            // rotation is relative to what a viewer showed.
            img.Mutate(x => x.AutoOrient());
            var mode = degrees switch { 90 => RotateMode.Rotate90, 180 => RotateMode.Rotate180, _ => RotateMode.Rotate270 };
            img.Mutate(x => x.Rotate(mode));
            return Save(img, kind, namer.Reserve(input, "-rotated", ImageIo.Extension(kind)));
        }
    }

    // ---- compress --------------------------------------------------------------------------------

    private static string CompressOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var level = ImageIo.StrOpt(rq.Options, "level", "recommended").ToLowerInvariant();
        var (img, kind) = ImageIo.Load(input);
        string outPath;
        using (img)
        {
            if (kind == ImageKind.Bmp)
                throw new NotSupportedException("bmp has no compression to apply; convert it to png or jpg first: " + input);
            img.Mutate(x => x.AutoOrient());
            img.Metadata.ExifProfile = null;
            outPath = namer.Reserve(input, "-compressed", ImageIo.Extension(kind));
            switch (kind)
            {
                case ImageKind.Jpeg:
                {
                    var q = level switch { "extreme" => 45, "low" => 88, _ => 72 };
                    img.Save(outPath, new JpegEncoder { Quality = q });
                    break;
                }
                case ImageKind.Png:
                {
                    var enc = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                    if (level != "low")
                    {
                        enc.ColorType = PngColorType.Palette;
                        enc.Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = level == "extreme" ? 64 : 256 });
                    }
                    img.Save(outPath, enc);
                    break;
                }
                default:
                {
                    var colors = level switch { "extreme" => 32, "low" => 256, _ => 128 };
                    img.Save(outPath, new GifEncoder { Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = colors }) });
                    break;
                }
            }
        }

        var before = new System.IO.FileInfo(input).Length;
        var after = new System.IO.FileInfo(outPath).Length;
        if (after >= before)
        {
            IoFile.Delete(outPath);
            throw new InvalidOperationException(
                $"could not make {input} smaller at level '{level}' ({before} bytes in, {after} bytes out); it is already compact");
        }
        return outPath;
    }

    // ---- conversions -----------------------------------------------------------------------------

    private static Image<Rgba32> FirstFrame(Image<Rgba32> img) =>
        img.Frames.Count > 1 ? img.Frames.CloneFrame(0) : img.Clone();

    private static string ToJpg(string input, string suffix, ToolRequest rq, ImageIo.OutputNamer namer, bool refuseJpg = false)
    {
        var quality = ImageIo.IntOpt(rq.Options, "quality", 90);
        var bg = ImageIo.ParseOpaqueHex("background", ImageIo.StrOpt(rq.Options, "background", "#FFFFFF"));
        var (src, srcKind) = ImageIo.Load(input);
        using (src)
        using (var flat = FirstFrame(src))
        {
            if (refuseJpg && srcKind == ImageKind.Jpeg)
                throw new InvalidOperationException("input is already jpg; pick a different option 'to': " + input);
            flat.Mutate(x => x.AutoOrient());
            flat.Mutate(x => x.BackgroundColor(bg));
            // BackgroundColor paints behind the image; flatten any remaining alpha so the JPG is opaque.
            return Save(flat, ImageKind.Jpeg, namer.Reserve(input, suffix, ".jpg"), quality);
        }
    }

    private static string ToJpgOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        ToJpg(input, "-jpg", rq, namer);

    private static string FromJpgOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var target = ImageKindOpt(rq, "format", "png");
        var (src, kind) = ImageIo.Load(input);
        using (src)
        {
            if (kind != ImageKind.Jpeg)
                throw new InvalidOperationException("input is not a JPG (it is " + kind.ToString().ToLowerInvariant() + "): " + input);
            src.Mutate(x => x.AutoOrient());
            return Save(src, target, namer.Reserve(input, "-" + target.ToString().ToLowerInvariant(), ImageIo.Extension(target)));
        }
    }

    private static ImageKind ImageKindOpt(ToolRequest rq, string key, string fallback) =>
        ImageIo.KindFromName(ImageIo.StrOpt(rq.Options, key, fallback));

    private static string ConvertAnyOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var target = ImageKindOpt(rq, "to", "png");
        if (target == ImageKind.Jpeg) return ToJpg(input, "-converted", rq, namer, true);
        var (src, kind) = ImageIo.Load(input);
        using (src)
        {
            if (kind == target)
                throw new InvalidOperationException(
                    $"input is already {target.ToString().ToLowerInvariant()}; pick a different option 'to': {input}");
            src.Mutate(x => x.AutoOrient());
            if (target == ImageKind.Gif || target == ImageKind.Bmp || target == ImageKind.Png)
            {
                using var frame = FirstFrame(src);
                return Save(frame, target, namer.Reserve(input, "-converted", ImageIo.Extension(target)));
            }
            throw new ArgumentException("option 'to' is not supported: " + target);
        }
    }

    // ---- watermark -------------------------------------------------------------------------------

    private static string WatermarkOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var o = rq.Options;
        if (o.TryGetValue("text", out var text) && !string.IsNullOrEmpty(text))
            throw new NotSupportedException("text watermarks are not built in this version; give option 'image' instead");
        if (!o.TryGetValue("image", out var markPath) || string.IsNullOrEmpty(markPath))
            throw new ArgumentException("option 'image' is required (a png, jpg, gif or bmp to stamp)");

        var opacity = ImageIo.IntOpt(o, "opacity", 50) / 100f;
        var scale = ImageIo.IntOpt(o, "scale", 25);
        var position = ImageIo.StrOpt(o, "position", "center");

        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            var (mark, _) = ImageIo.Load(markPath);
            using (mark)
            {
                img.Mutate(x => x.AutoOrient());
                var (mw, mh) = ImageGeometry.OverlaySize(img.Width, img.Height, mark.Width, mark.Height, scale);
                mark.Mutate(x => x.Resize(mw, mh, KnownResamplers.Lanczos3));
                var (px, py) = ImageGeometry.OverlayOrigin(img.Width, img.Height, mw, mh, position);
                img.Mutate(x => x.DrawImage(mark, new Point(px, py), opacity));
                return Save(img, kind, namer.Reserve(input, "-watermarked", ImageIo.Extension(kind)));
            }
        }
    }

    // ---- dpi -------------------------------------------------------------------------------------

    private static string DpiOne(string input, ToolRequest rq, ImageIo.OutputNamer namer)
    {
        var dpi = ImageIo.IntOpt(rq.Options, "dpi", 300);
        var (img, kind) = ImageIo.Load(input);
        using (img)
        {
            if (kind == ImageKind.Gif)
                throw new NotSupportedException("gif files carry no resolution; use png or jpg: " + input);
            img.Mutate(x => x.AutoOrient());
            img.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
            img.Metadata.HorizontalResolution = dpi;
            img.Metadata.VerticalResolution = dpi;
            // JPG is re-encoded at the top quality setting; PNG and BMP are lossless.
            return Save(img, kind, namer.Reserve(input, "-" + dpi + "dpi", ImageIo.Extension(kind)), 100);
        }
    }
}
