using System;
using System.Collections.Generic;
using System.IO;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NetieSpace.Apps.Image;

/// <summary>How a tool is shown on the page: before and after side by side, a draggable box, or a picture with a position pad.</summary>
public enum FlowStage { None, BeforeAfter, Crop, Watermark }

/// <summary>A picture ready to draw (PNG bytes, at most the proxy size) and the real pixel size the run will produce.</summary>
public sealed record FlowFrame(byte[] Png, int PixelWidth, int PixelHeight);

/// <summary>What a run would write, measured by running it in memory. Never throws: a refusal is in <see cref="Error"/>.</summary>
public sealed record FlowSize(long? Bytes, int PixelWidth, int PixelHeight, EncodeSpec? Spec, string? Note, string? Error);

/// <summary>
/// The visual half of the image tools. A <see cref="FlowSource"/> holds one decoded, upright, reduced copy of a photo and
/// draws what a tool would make from it fast enough to follow typing; <see cref="ImageFlow.Measure"/> runs the real
/// function on the real file for the size in KB. Both go through <see cref="ImageOps"/>, the code the runners use.
/// </summary>
public static class ImageFlow
{
    public static FlowStage StageOf(string toolId) => toolId switch
    {
        "img-crop" => FlowStage.Crop,
        "img-watermark" => FlowStage.Watermark,
        "img-resize" or "img-rotate" or "img-compress" or "img-convert-any" or "img-to-jpg" or "img-from-jpg" => FlowStage.BeforeAfter,
        _ => FlowStage.None,
    };

    public static bool Supports(string toolId) => StageOf(toolId) != FlowStage.None;

    /// <summary>The tools whose result size is worth showing before a run.</summary>
    public static bool ShowsSize(string toolId) => Supports(toolId) && toolId != "img-crop" && toolId != "img-rotate";

    /// <summary>Runs the tool on the real file in memory: the size a run would write, or why it would refuse.</summary>
    public static FlowSize Measure(string path, string toolId, IReadOnlyDictionary<string, string> options)
    {
        try
        {
            var f = Run(path, toolId, options);
            return new FlowSize(f.Bytes.LongLength, f.Width, f.Height, f.Spec, f.Note, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FlowSize(null, 0, 0, null, null, Plain(ex));
        }
    }

    /// <summary>The one dispatch both the runner tests and the page use: tool id and options to a finished picture.</summary>
    public static OutFile Run(string path, string toolId, IReadOnlyDictionary<string, string> options) => toolId switch
    {
        "img-resize" => ImageOps.ResizeFile(path, options),
        "img-crop" => ImageOps.CropFile(path, options),
        "img-rotate" => ImageOps.RotateFile(path, options),
        "img-compress" => ImageOps.CompressFile(path, options),
        "img-watermark" => ImageOps.WatermarkFile(path, options),
        "img-convert-any" => ImageOps.ConvertAnyFile(path, options),
        "img-to-jpg" => ImageOps.ToJpgFile(path, "-jpg", options, false),
        "img-from-jpg" => ImageOps.FromJpgFile(path, options),
        _ => throw new NotSupportedException("no preview for " + toolId),
    };

    /// <summary>
    /// The options a run would use for this file: the page's values checked and completed by the same validation a run goes
    /// through (defaults filled, values canonical), or the message a run would stop with.
    /// </summary>
    public static (IReadOnlyDictionary<string, string>? Options, string? Error) Resolve(
        ToolEntry entry, string file, IReadOnlyDictionary<string, string> options)
    {
        var msg = ToolRunner.Validate(entry, new ToolRequest(new[] { file }, "preview", options), out var resolved);
        return msg is null ? (resolved.Options, null) : (null, msg);
    }

    /// <summary>The runner's message without the type name, for a line a person reads.</summary>
    public static string Plain(Exception ex) => ex.Message;

    internal static byte[] ToPng(Image<Rgba32> img)
    {
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return ms.ToArray();
    }
}

/// <summary>One photo, opened once: upright, reduced to <see cref="ProxyMax"/> on its long side, with the real size kept.</summary>
public sealed class FlowSource : IDisposable
{
    public const int DefaultProxyMax = 960;
    private readonly Image<Rgba32> _proxy;
    private readonly object _gate = new();
    private bool _disposed;

    private FlowSource(string path, ImageKind kind, int w, int h, long bytes, Image<Rgba32> proxy)
    {
        Path = path; Kind = kind; Width = w; Height = h; FileBytes = bytes; _proxy = proxy;
    }

    public string Path { get; }
    public ImageKind Kind { get; }
    /// <summary>The picture's real size once it is upright (what the tools work on).</summary>
    public int Width { get; }
    public int Height { get; }
    public long FileBytes { get; }
    public int ProxyWidth => _proxy.Width;
    public int ProxyHeight => _proxy.Height;

    public static FlowSource Open(string path, int proxyMax = DefaultProxyMax)
    {
        var (img, kind) = ImageIo.Load(path);
        using (img)
        {
            img.Mutate(x => x.AutoOrient());
            var w = img.Width;
            var h = img.Height;
            var proxy = ImageOps.FirstFrame(img);
            if (Math.Max(w, h) > proxyMax)
            {
                var s = (double)proxyMax / Math.Max(w, h);
                proxy.Mutate(x => x.Resize(Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)), KnownResamplers.Triangle));
            }
            return new FlowSource(path, kind, w, h, new FileInfo(path).Length, proxy);
        }
    }

    public FlowFrame Before()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new(ImageFlow.ToPng(_proxy), Width, Height);
        }
    }

    /// <summary>
    /// What the tool would make, drawn from the reduced copy. Options are applied exactly as the runner applies them
    /// (a text mark is sized by its share of the width, a box is a fraction of the picture), so it matches the file.
    /// <paramref name="measured"/> carries the encoder setting a size search found, so a compressed picture shows the same loss.
    /// Throws what the runner would throw for options it refuses.
    /// </summary>
    public FlowFrame After(string toolId, IReadOnlyDictionary<string, string> o, EncodeSpec? measured = null)
    {
        Image<Rgba32> copy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            copy = _proxy.Clone();
        }
        using var work = copy;
        switch (toolId)
        {
            case "img-resize":
            {
                var (tw, th) = TargetSize(o);
                var s = Math.Min(1.0, (double)FlowSource.DefaultProxyMax / Math.Max(tw, th));
                var pw = Math.Max(1, (int)Math.Round(tw * s));
                var ph = Math.Max(1, (int)Math.Round(th * s));
                var cover = IsCover(o);
                work.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(pw, ph),
                    Mode = cover ? ResizeMode.Crop : ResizeMode.Stretch,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Triangle,
                }));
                return new FlowFrame(ImageFlow.ToPng(work), tw, th);
            }
            case "img-crop":
            {
                ImageOps.CheckCropOptions(o);
                var (cx, cy, cw, ch) = ImageOps.CropRectFor(Width, Height, o);
                ImageGeometry.CheckCrop(Width, Height, cx, cy, cw, ch);
                var fx = (double)work.Width / Width;
                var fy = (double)work.Height / Height;
                var rx = Math.Clamp((int)Math.Round(cx * fx), 0, work.Width - 1);
                var ry = Math.Clamp((int)Math.Round(cy * fy), 0, work.Height - 1);
                var rw = Math.Clamp((int)Math.Round(cw * fx), 1, work.Width - rx);
                var rh = Math.Clamp((int)Math.Round(ch * fy), 1, work.Height - ry);
                work.Mutate(x => x.Crop(new Rectangle(rx, ry, rw, rh)));
                return new FlowFrame(ImageFlow.ToPng(work), cw, ch);
            }
            case "img-rotate":
            {
                var degrees = ImageGeometry.RotationDegrees(ImageIo.StrOpt(o, "direction", "right"));
                ImageOps.Rotate(work, degrees);
                return new FlowFrame(ImageFlow.ToPng(work), degrees == 180 ? Width : Height, degrees == 180 ? Height : Width);
            }
            case "img-watermark":
            {
                var plan = ImageOps.PlanWatermark(o);
                ImageOps.Watermark(work, plan);
                return new FlowFrame(ImageFlow.ToPng(work), Width, Height);
            }
            case "img-compress":
            {
                if (Kind == ImageKind.Bmp)
                    throw new NotSupportedException("bmp has no compression to apply; convert it to png or jpg first");
                var spec = measured ?? ImageOps.BaseCompressSpec(Kind, ImageIo.StrOpt(o, "level", "recommended"));
                return new FlowFrame(Roundtrip(work, spec), Width, Height);
            }
            case "img-to-jpg":
            case "img-convert-any":
            case "img-from-jpg":
            {
                var spec = ConvertSpec(toolId, o);
                if (spec.Kind == ImageKind.Jpeg)
                {
                    var bg = ImageIo.ParseOpaqueHex("background", ImageIo.StrOpt(o, "background", "#FFFFFF"));
                    work.Mutate(x => x.BackgroundColor(bg));
                }
                return new FlowFrame(Roundtrip(work, spec), Width, Height);
            }
            default:
                throw new NotSupportedException("no preview for " + toolId);
        }
    }

    private EncodeSpec ConvertSpec(string toolId, IReadOnlyDictionary<string, string> o)
    {
        switch (toolId)
        {
            case "img-to-jpg":
                return ImageOps.DefaultSpec(ImageKind.Jpeg, ImageIo.IntOpt(o, "quality", 90));
            case "img-from-jpg":
            {
                if (Kind != ImageKind.Jpeg)
                    throw new InvalidOperationException("input is not a JPG (it is " + Kind.ToString().ToLowerInvariant() + ")");
                return ImageOps.DefaultSpec(ImageIo.KindFromName(ImageIo.StrOpt(o, "format", "png")));
            }
            default:
            {
                var target = ImageIo.KindFromName(ImageIo.StrOpt(o, "to", "png"));
                if (target == Kind && target != ImageKind.Jpeg)
                    throw new InvalidOperationException($"input is already {target.ToString().ToLowerInvariant()}; pick a different Convert to");
                if (target == ImageKind.Jpeg && Kind == ImageKind.Jpeg)
                    throw new InvalidOperationException("input is already jpg; pick a different Convert to");
                return ImageOps.DefaultSpec(target, ImageIo.IntOpt(o, "quality", 90));
            }
        }
    }

    private static bool IsCover(IReadOnlyDictionary<string, string> o) =>
        string.Equals(ImageIo.StrOpt(o, "fit", "inside"), "cover", StringComparison.OrdinalIgnoreCase)
        && string.Equals(ImageIo.StrOpt(o, "mode", "pixels"), "pixels", StringComparison.OrdinalIgnoreCase);

    private (int W, int H) TargetSize(IReadOnlyDictionary<string, string> o)
    {
        var width = ImageIo.IntOpt(o, "width");
        var height = ImageIo.IntOpt(o, "height");
        if (IsCover(o))
        {
            if (width < 1) throw new ArgumentException("option 'width' is required to fill a frame");
            if (height < 1) throw new ArgumentException("option 'height' is required to fill a frame");
            ImageIo.CheckPixelBudget(width, height, "the resized image");
            return (width, height);
        }
        var (w, h) = ImageGeometry.ResizeTarget(Width, Height, ImageIo.StrOpt(o, "mode", "pixels"), width, height,
            ImageIo.IntOpt(o, "percent", 50), ImageIo.BoolOpt(o, "keep_ratio", true));
        ImageIo.CheckPixelBudget(w, h, "the resized image");
        return (w, h);
    }

    private static byte[] Roundtrip(Image<Rgba32> img, EncodeSpec spec)
    {
        var encoded = ImageOps.Encode(img, spec);
        using var back = SixLabors.ImageSharp.Image.Load<Rgba32>(encoded);
        return ImageFlow.ToPng(back);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _proxy.Dispose();
        }
    }
}
