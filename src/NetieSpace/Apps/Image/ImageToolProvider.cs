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

    /// <summary>Writes a finished picture under the next free name; the bytes are what <see cref="ImageOps"/> measured.</summary>
    private static string Write(OutFile f, string input, ImageIo.OutputNamer namer)
    {
        var path = namer.Reserve(input, f.Suffix, ImageIo.Extension(f.Kind));
        IoFile.WriteAllBytes(path, f.Bytes);
        return path;
    }

    private static string ResizeOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.ResizeFile(input, rq.Options), input, namer);

    private static string CropOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.CropFile(input, rq.Options), input, namer);

    private static string RotateOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.RotateFile(input, rq.Options), input, namer);

    private static string CompressOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.CompressFile(input, rq.Options), input, namer);

    // ---- conversions -----------------------------------------------------------------------------

    private static string ToJpgOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.ToJpgFile(input, "-jpg", rq.Options, false), input, namer);

    private static string FromJpgOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.FromJpgFile(input, rq.Options), input, namer);

    private static string ConvertAnyOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.ConvertAnyFile(input, rq.Options), input, namer);

    // ---- watermark -------------------------------------------------------------------------------

    private static string WatermarkOne(string input, ToolRequest rq, ImageIo.OutputNamer namer) =>
        Write(ImageOps.WatermarkFile(input, rq.Options), input, namer);

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
