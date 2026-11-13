using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>In-place page edits on PdfSharpCore: rotate, page numbers, watermark, crop.</summary>
internal static class PdfEditTools
{
    // ----- pdf-rotate -----

    public static Task<IReadOnlyList<string>> RotateAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var direction = OptChoice(req, "direction", "right", "left", "right");
            var filter = OptChoice(req, "filter", "all", "all", "portrait", "landscape");
            var spec = Opt(req, "pages", "1-end");
            var delta = direction == "right" ? 90 : 270;
            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                ct.ThrowIfCancellationRequested();
                var input = req.Files[f];
                using var doc = OpenModify(input);
                var targets = ParsePages(spec, doc.PageCount, "pages");
                var touched = 0;
                var boxes = Enumerable.Range(0, doc.PageCount)
                    .Select(i => (Media: doc.Pages[i].MediaBox, Crop: doc.Pages[i].CropBox)).ToList();
                foreach (var p in targets.Distinct())
                {
                    var page = doc.Pages[p - 1];
                    var (w, h) = VisualSize(page);
                    var portrait = h >= w;
                    if (filter == "portrait" && !portrait) continue;
                    if (filter == "landscape" && portrait) continue;
                    SetRotate(page, (((page.Rotate + delta) % 360) + 360) % 360);
                    touched++;
                }
                if (touched == 0)
                    throw new InvalidOperationException($"{IoPath.GetFileName(input)}: no page matched pages '{spec}' with filter '{filter}'");
                var path = names.Next(Stem(input) + "_rotated", ".pdf");
                doc.Save(path);
                VerifyBoxes(path, boxes, input);
                outputs.Add(path);
                Report(progress, f + 1, req.Files.Count, "rotated " + IoPath.GetFileName(input));
            }
            return outputs;
        });

    /// <summary>Reads the saved file back and refuses it (deleting it) when a page box differs from the input's.</summary>
    private static void VerifyBoxes(string output, List<(PdfRectangle Media, PdfRectangle Crop)> expected, string input)
    {
        static bool Same(PdfRectangle a, PdfRectangle b) =>
            Math.Abs(a.X1 - b.X1) < 0.5 && Math.Abs(a.Y1 - b.Y1) < 0.5 && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;
        string? problem = null;
        using (var back = OpenImport(output))
        {
            if (back.PageCount != expected.Count) problem = "the page count changed";
            else
                for (var i = 0; i < expected.Count && problem is null; i++)
                {
                    if (!Same(back.Pages[i].MediaBox, expected[i].Media)) problem = $"page {i + 1} changed size";
                    else if (!expected[i].Crop.IsEmpty && !Same(back.Pages[i].CropBox, expected[i].Crop)) problem = $"page {i + 1} changed its crop area";
                }
        }
        if (problem is null) return;
        try { IoFile.Delete(output); } catch (IOException) { }
        throw new InvalidOperationException($"cannot rotate {IoPath.GetFileName(input)}: {problem} while rotating, so the result was discarded");
    }

    // ----- pdf-page-numbers -----

    public static Task<IReadOnlyList<string>> PageNumbersAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var (vert, horiz) = ParseGrid(Opt(req, "position", "bottom-center"), "position", allowMiddle: false);
            var start = OptInt(req, "start", 1, 0, 1_000_000);
            var size = OptInt(req, "size", 10, 4, 96);
            var format = OptChoice(req, "format", "n", "n", "page-n", "n-of-total");
            var color = ParseColor(Opt(req, "color", "#000000"), "color");
            var spec = Opt(req, "pages", "1-end");
            var font = ResolveFont(size);
            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                var input = req.Files[f];
                using var doc = OpenModify(input);
                var targets = ParsePages(spec, doc.PageCount, "pages").Distinct().OrderBy(p => p).ToList();
                var number = start;
                var total = start + targets.Count - 1;
                var brush = new XSolidBrush(color);
                foreach (var p in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    var text = format switch
                    {
                        "page-n" => "Page " + number,
                        "n-of-total" => number + " of " + total,
                        _ => number.ToString(CultureInfo.InvariantCulture),
                    };
                    using var gfx = OpenVisualGraphics(doc.Pages[p - 1], true, out var vw, out var vh);
                    var m = gfx.MeasureString(text, font);
                    const double margin = 28;
                    var x = horiz switch { "left" => margin, "right" => vw - margin - m.Width, _ => (vw - m.Width) / 2 };
                    var yTop = vert == "top" ? margin : vh - margin - m.Height;
                    gfx.DrawString(text, font, brush, new XPoint(x, yTop + font.GetHeight() * 0.8));
                    number++;
                    Report(progress, p, doc.PageCount, "numbered page " + p);
                }
                var path = names.Next(Stem(input) + "_numbered", ".pdf");
                doc.Save(path);
                outputs.Add(path);
            }
            return outputs;
        });

    /// <summary>"top-left" style position to (vertical, horizontal). Netie's own 3x3 grid.</summary>
    internal static (string Vert, string Horiz) ParseGrid(string text, string key, bool allowMiddle)
    {
        var parts = (text ?? "").ToLowerInvariant().Split('-');
        var okV = parts.Length == 2 && (parts[0] is "top" or "bottom" || (allowMiddle && parts[0] == "middle"));
        var okH = parts.Length == 2 && parts[1] is "left" or "center" or "right";
        if (!okV || !okH)
            throw new ArgumentException($"option '{key}': '{text}' must be like top-left, bottom-center" + (allowMiddle ? ", middle-center" : ""));
        return (parts[0], parts[1]);
    }

    // ----- pdf-watermark -----

    public static Task<IReadOnlyList<string>> WatermarkAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var mode = OptChoice(req, "mode", "text", "text", "image");
            var opacity = OptInt(req, "opacity", 50, 1, 100);
            var rotation = OptInt(req, "rotation", 45, 0, 360);
            var (vert, horiz) = ParseGrid(Opt(req, "position", "middle-center"), "position", allowMiddle: true);
            var tile = OptBool(req, "tile", false);
            var above = OptChoice(req, "layer", "above", "above", "below") == "above";
            var spec = Opt(req, "pages", "1-end");

            string text = "";
            XFont? font = null;
            XBrush? brush = null;
            byte[]? imageBytes = null;
            var scale = 40;
            if (mode == "text")
            {
                text = Opt(req, "text", "DRAFT");
                if (text.Trim().Length == 0) throw new ArgumentException("option 'text': watermark text is empty");
                // The default PDF font encoding is WinAnsi: a character outside Latin-1 would be silently
                // replaced by a wrong glyph, so refuse it instead of stamping the wrong word.
                foreach (var ch in text)
                    if (!((ch >= ' ' && ch <= '~') || (ch >= '\u00A0' && ch <= '\u00FF')))
                        throw new ArgumentException($"option 'text': the character U+{(int)ch:X4} cannot be drawn (only Latin letters, digits and punctuation up to U+00FF are supported)");
                font = ResolveFont(OptInt(req, "size", 64, 6, 400), XFontStyle.Bold);
                brush = new XSolidBrush(ParseColor(Opt(req, "color", "#808080"), "color", opacity));
            }
            else
            {
                var imgPath = Opt(req, "image");
                if (imgPath.Length == 0) throw new ArgumentException("option 'image': give an image file for mode=image");
                if (!IoFile.Exists(imgPath)) throw new ArgumentException("option 'image': file not found: " + imgPath);
                scale = OptInt(req, "scale", 40, 5, 100);
                imageBytes = PrepareWatermarkImage(imgPath, opacity);
            }

            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                var input = req.Files[f];
                using var doc = OpenModify(input);
                var targets = ParsePages(spec, doc.PageCount, "pages").Distinct().ToList();
                var done = 0;
                foreach (var p in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    using var gfx = OpenVisualGraphics(doc.Pages[p - 1], above, out var vw, out var vh);
                    double mw, mh;
                    XImage? img = null;
                    if (mode == "text")
                    {
                        var sz = gfx.MeasureString(text, font!);
                        mw = sz.Width; mh = sz.Height;
                    }
                    else
                    {
                        img = XImage.FromStream(() => new MemoryStream(imageBytes!));
                        mw = vw * scale / 100.0;
                        mh = mw * img.PixelHeight / Math.Max(1, img.PixelWidth);
                        if (mh > vh * 0.9) { mh = vh * 0.9; mw = mh * img.PixelWidth / Math.Max(1, img.PixelHeight); }
                    }

                    void Stamp(double cx, double cy)
                    {
                        var state = gfx.Save();
                        if (rotation != 0) gfx.RotateAtTransform(-rotation, new XPoint(cx, cy));
                        if (mode == "text")
                            gfx.DrawString(text, font!, brush!, new XPoint(cx - mw / 2, cy - mh / 2 + font!.GetHeight() * 0.8));
                        else
                            gfx.DrawImage(img!, cx - mw / 2, cy - mh / 2, mw, mh);
                        gfx.Restore(state);
                    }

                    if (tile)
                    {
                        var stepX = mw * 1.6 + 20;
                        var stepY = Math.Max(mh * 2.4, 90);
                        var row = 0;
                        for (var y = stepY / 2; y < vh + stepY; y += stepY, row++)
                            for (var x = (row % 2 == 0 ? 0 : stepX / 2) + stepX / 2; x < vw + stepX; x += stepX)
                                Stamp(x, y);
                    }
                    else
                    {
                        const double edge = 24;
                        var cx = horiz switch { "left" => edge + mw / 2, "right" => vw - edge - mw / 2, _ => vw / 2 };
                        var cy = vert switch { "top" => edge + mh / 2, "bottom" => vh - edge - mh / 2, _ => vh / 2 };
                        Stamp(cx, cy);
                    }
                    img?.Dispose();
                    Report(progress, ++done, targets.Count, "watermarked page " + p);
                }
                var path = names.Next(Stem(input) + "_watermarked", ".pdf");
                doc.Save(path);
                outputs.Add(path);
            }
            return outputs;
        });

    /// <summary>Decodes the watermark image and bakes the opacity into its alpha channel, returned as PNG.</summary>
    private static byte[] PrepareWatermarkImage(string path, int opacityPercent)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            image.Mutate(x => x.Opacity(opacityPercent / 100f));
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"option 'image': cannot decode {path}: {ex.Message}");
        }
    }

    // ----- pdf-crop -----

    public static Task<IReadOnlyList<string>> CropAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var left = OptInt(req, "left", 0, 0, 20000);
            var top = OptInt(req, "top", 0, 0, 20000);
            var right = OptInt(req, "right", 0, 0, 20000);
            var bottom = OptInt(req, "bottom", 0, 0, 20000);
            if (left + top + right + bottom == 0)
                throw new ArgumentException("option 'left'/'top'/'right'/'bottom': give at least one margin (in points) to cut off");
            var spec = Opt(req, "pages", "1-end");
            var input = req.Files[0];
            using var doc = OpenModify(input);
            var targets = ParsePages(spec, doc.PageCount, "pages").Distinct().ToList();
            var done = 0;
            foreach (var p in targets)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.Pages[p - 1];
                EffectiveBox(page, out var x0, out var y0, out var w, out var h);
                // Margins are given as displayed; turn them into margins of the unrotated page.
                double l, t, r, b;
                switch (((page.Rotate % 360) + 360) % 360)
                {
                    case 90: l = top; b = left; t = right; r = bottom; break;
                    case 180: l = right; r = left; t = bottom; b = top; break;
                    case 270: t = left; r = top; b = right; l = bottom; break;
                    default: l = left; t = top; r = right; b = bottom; break;
                }
                if (l + r >= w - 1 || t + b >= h - 1)
                    throw new ArgumentException($"option 'left'/'top'/'right'/'bottom': margins leave nothing of page {p}");
                // PDF user space is y-up: the lower-left corner moves up by the bottom margin.
                page.CropBox = new PdfRectangle(new XPoint(x0 + l, y0 + b), new XPoint(x0 + w - r, y0 + h - t));
                Report(progress, ++done, targets.Count, "cropped page " + p);
            }
            var path = new OutputNamer(req.OutDir).Next(Stem(input) + "_cropped", ".pdf");
            doc.Save(path);
            return new[] { path };
        });
}
