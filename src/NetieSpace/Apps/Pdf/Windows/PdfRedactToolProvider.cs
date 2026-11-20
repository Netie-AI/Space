using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp.PixelFormats;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using SharpDocument = PdfSharpCore.Pdf.PdfDocument;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// pdf-redact by rasterising: every page that has a box is rendered by Windows.Data.Pdf, the boxes are
/// painted into the pixels and the page is rebuilt as a picture page of the same size, so no text, vector
/// or hidden object of that page survives. Pages without a box are copied unchanged. Compiled only for
/// the Windows TFM.
/// </summary>
public sealed class PdfRedactToolProvider : IToolProvider
{
    // Beyond the glyph box: descenders, italics and anti-aliasing reach past it.
    private const double TextPadPoints = 1.5;
    private const double PointsPerUnit = 72.0 / WinPdfRender.PageUnitsPerInch;

    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-redact", (r, p, ct) => Task.Run(() => RunAsync(r, p, ct), ct));
    }

    /// <summary>A box on a page, in points from the top-left corner of the page as displayed.</summary>
    internal readonly record struct Box(double X, double Y, double W, double H);

    private static readonly Regex RegionToken = new(
        @"^(\d+)\s*:\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(\d+(?:\.\d+)?)\s*,\s*(\d+(?:\.\d+)?)$",
        RegexOptions.Compiled);

    /// <summary>"page:x,y,w,h;page:x,y,w,h" to boxes by 1-based page number. Throws naming option 'regions'.</summary>
    internal static List<(int Page, Box Box)> ParseRegions(string text)
    {
        var list = new List<(int, Box)>();
        foreach (var raw in text.Split(';'))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            var m = RegionToken.Match(token);
            if (!m.Success)
                throw new ArgumentException("option 'regions': '" + token + "' is not page:x,y,w,h in points");
            double N(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            var page = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var box = new Box(N(2), N(3), N(4), N(5));
            if (page < 1) throw new ArgumentException("option 'regions': page numbers start at 1 in '" + token + "'");
            if (box.W <= 0 || box.H <= 0)
                throw new ArgumentException("option 'regions': width and height must be above 0 in '" + token + "'");
            list.Add((page, box));
        }
        return list;
    }

    /// <summary>
    /// Maps a rectangle in PDF user space (origin bottom-left of the page box) to points from the top-left of
    /// the page as displayed, for a page rotated by <paramref name="rotation"/> degrees clockwise.
    /// </summary>
    internal static Box ToDisplay(
        double left, double bottom, double right, double top,
        double boxL, double boxB, double boxR, double boxT, int rotation)
    {
        (double X, double Y) P(double x, double y) => rotation switch
        {
            90 => (y - boxB, x - boxL),
            180 => (boxR - x, y - boxB),
            270 => (boxT - y, boxR - x),
            _ => (x - boxL, boxT - y),
        };
        var a = P(left, bottom);
        var b = P(right, top);
        var c = P(left, top);
        var d = P(right, bottom);
        var x0 = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var x1 = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        var y0 = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        var y1 = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }

    private sealed class PagePlan
    {
        public double WidthPt;
        public double HeightPt;
        public int LetterCount;
        public readonly List<Box> Boxes = new();
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var dpi = PdfToolSupport.OptInt(request, "dpi", 200, 100, 300);
        var color = PdfToolSupport.ParseColor(PdfToolSupport.Opt(request, "color", "#000000"), "color");
        if (color.A != 255)
            throw new ArgumentException("option 'color': a see-through colour would not hide anything; use #RRGGBB");
        var regionsText = PdfToolSupport.Opt(request, "regions", "");
        var terms = PdfToolSupport.Opt(request, "text", "")
            .Split(';').Select(t => Regex.Replace(t.Trim(), @"\s+", " ")).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var regions = ParseRegions(regionsText);
        if (regions.Count == 0 && terms.Count == 0)
            throw new ArgumentException("option 'regions' or 'text': give at least one area or one word to remove");
        if (request.Files.Count != 1)
            throw new ArgumentException("pdf-redact takes exactly one PDF");

        var input = IoPath.GetFullPath(request.Files[0]);

        // 1. Plan: page sizes, boxes from the areas and from every match of the text.
        List<PagePlan> plans;
        try
        {
            plans = PlanBoxes(input, regions, terms, ct);
        }
        catch (Exception ex) when (ex is not (ArgumentException or OperationCanceledException or InvalidOperationException))
        {
            throw new InvalidOperationException("cannot open " + input + " as a PDF: " + ex.Message, ex);
        }
        var boxed = plans.Count(p => p.Boxes.Count > 0);

        IoDirectory.CreateDirectory(request.OutDir);
        var temp = IoPath.Combine(request.OutDir, ".netie-redact-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? final = null;
        var images = new List<XImage>();
        try
        {
            // 2. Rebuild: boxed pages as pictures, the rest copied.
            var document = await WinPdfRender.OpenAsync(input, ct);
            if ((int)document.PageCount != plans.Count)
                throw new InvalidOperationException(
                    "the Windows PDF renderer sees " + document.PageCount + " pages in " + input + " but the text reader sees "
                    + plans.Count + "; nothing was written");

            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            using var output = new SharpDocument();
            var done = 0;
            for (var i = 0; i < plans.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var plan = plans[i];
                if (plan.Boxes.Count == 0)
                {
                    output.AddPage(source.Pages[i]);
                    continue;
                }

                var png = await RenderRedactedPageAsync(document, i, plan, dpi, color, input, ct);
                var page = output.AddPage();
                page.Width = plan.WidthPt;
                page.Height = plan.HeightPt;
                var image = XImage.FromStream(() => new System.IO.MemoryStream(png));
                images.Add(image);
                using (var gfx = XGraphics.FromPdfPage(page))
                    gfx.DrawImage(image, 0, 0, plan.WidthPt, plan.HeightPt);
                done++;
                progress?.Report(new ToolProgress(done * 90 / boxed, $"page {i + 1} of {plans.Count}"));
            }
            output.Save(temp);

            // 3. Prove it on the file that will be handed over.
            VerifyOutput(temp, plans, terms);

            final = WinPdfRender.FreePath(request.OutDir, IoPath.GetFileNameWithoutExtension(input) + "-redacted", ".pdf", Array.Empty<string>());
            IoFile.Move(temp, final);
            progress?.Report(new ToolProgress(100, $"{boxed} of {plans.Count} pages redacted"));
            return new[] { final };
        }
        catch
        {
            WinPdfRender.DeleteQuietly(new[] { temp });
            if (final is not null) WinPdfRender.DeleteQuietly(new[] { final });
            throw;
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    private static List<PagePlan> PlanBoxes(
        string input, List<(int Page, Box Box)> regions, List<string> terms, CancellationToken ct)
    {
        var plans = new List<PagePlan>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var pig = PigDocument.Open(input);
        for (var n = 1; n <= pig.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pig.GetPage(n);
            var rotation = ((page.Rotation.Value % 360) + 360) % 360;
            var crop = page.CropBox.Bounds;
            var rawW = crop.Right - crop.Left;
            var rawH = crop.Top - crop.Bottom;
            var swap = rotation is 90 or 270;
            var plan = new PagePlan
            {
                WidthPt = swap ? rawH : rawW,
                HeightPt = swap ? rawW : rawH,
                LetterCount = page.Letters.Count,
            };
            plans.Add(plan);

            if (terms.Count > 0)
            {
                var words = page.GetWords().ToList();
                var text = new StringBuilder();
                var starts = new List<int>();
                foreach (var w in words)
                {
                    if (text.Length > 0) text.Append(' ');
                    starts.Add(text.Length);
                    text.Append(w.Text);
                }
                var haystack = text.ToString();
                foreach (var term in terms)
                {
                    var at = 0;
                    while (at <= haystack.Length - term.Length
                           && (at = haystack.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        found.Add(term);
                        var end = at + term.Length;
                        for (var k = 0; k < words.Count; k++)
                        {
                            var ws = starts[k];
                            var we = ws + words[k].Text.Length;
                            if (ws >= end || we <= at) continue;
                            var b = words[k].BoundingBox;
                            var l = Math.Min(Math.Min(b.BottomLeft.X, b.TopLeft.X), Math.Min(b.BottomRight.X, b.TopRight.X));
                            var r = Math.Max(Math.Max(b.BottomLeft.X, b.TopLeft.X), Math.Max(b.BottomRight.X, b.TopRight.X));
                            var lo = Math.Min(Math.Min(b.BottomLeft.Y, b.TopLeft.Y), Math.Min(b.BottomRight.Y, b.TopRight.Y));
                            var hi = Math.Max(Math.Max(b.BottomLeft.Y, b.TopLeft.Y), Math.Max(b.BottomRight.Y, b.TopRight.Y));
                            var box = ToDisplay(l, lo, r, hi, crop.Left, crop.Bottom, crop.Right, crop.Top, rotation);
                            plan.Boxes.Add(new Box(
                                box.X - TextPadPoints, box.Y - TextPadPoints,
                                box.W + 2 * TextPadPoints, box.H + 2 * TextPadPoints));
                        }
                        at += Math.Max(1, term.Length);
                    }
                }
            }
        }

        var missing = terms.Where(t => !found.Contains(t)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "option 'text': " + string.Join(", ", missing.Select(t => "'" + t + "'"))
                + " not found in the text of " + input + "; nothing was written (a scanned page has no text: use option 'regions')");

        foreach (var (pageNumber, box) in regions)
        {
            if (pageNumber > plans.Count)
                throw new ArgumentException("option 'regions': page " + pageNumber + " does not exist, " + input + " has " + plans.Count + " pages");
            var plan = plans[pageNumber - 1];
            if (box.X + box.W <= 0 || box.Y + box.H <= 0 || box.X >= plan.WidthPt || box.Y >= plan.HeightPt)
                throw new ArgumentException(
                    $"option 'regions': the area on page {pageNumber} lies outside the page ({plan.WidthPt:0.#} x {plan.HeightPt:0.#} points)");
            plan.Boxes.Add(box);
        }
        return plans;
    }

    private static async Task<byte[]> RenderRedactedPageAsync(
        Windows.Data.Pdf.PdfDocument document, int index, PagePlan plan, int dpi,
        XColor color, string input, CancellationToken ct)
    {
        using var page = document.GetPage((uint)index);
        // The two readers must agree on the page, or the boxes would land somewhere else.
        var renderW = page.Size.Width * PointsPerUnit;
        var renderH = page.Size.Height * PointsPerUnit;
        if (Math.Abs(renderW - plan.WidthPt) > Math.Max(2.0, plan.WidthPt * 0.01)
            || Math.Abs(renderH - plan.HeightPt) > Math.Max(2.0, plan.HeightPt * 0.01))
            throw new InvalidOperationException(
                $"page {index + 1} of {input} is {renderW:0.#} x {renderH:0.#} points to the renderer but {plan.WidthPt:0.#} x {plan.HeightPt:0.#} to the text reader; boxes cannot be placed safely, nothing was written");

        var (width, height) = WinPdfRender.PixelSize(page, dpi, input, index + 1);
        using var stream = await WinPdfRender.RenderAsync(
            page, width, height, Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, index + 1, input, ct);
        using var read = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(read);
        if (image.Width != (int)width || image.Height != (int)height)
            throw new InvalidOperationException($"page {index + 1} of {input} rendered at {image.Width}x{image.Height}, expected {width}x{height}");

        Paint(image, plan, color);
        using var ms = new System.IO.MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, ms);
        return ms.ToArray();
    }

    private static void Paint(SixLabors.ImageSharp.Image<Rgb24> image, PagePlan plan, XColor color)
    {
        var paint = new Rgb24(color.R, color.G, color.B);
        var sx = image.Width / plan.WidthPt;
        var sy = image.Height / plan.HeightPt;
        foreach (var box in plan.Boxes)
        {
            var x0 = Math.Max(0, (int)Math.Floor(box.X * sx) - 1);
            var x1 = Math.Min(image.Width, (int)Math.Ceiling((box.X + box.W) * sx) + 1);
            var y0 = Math.Max(0, (int)Math.Floor(box.Y * sy) - 1);
            var y1 = Math.Min(image.Height, (int)Math.Ceiling((box.Y + box.H) * sy) + 1);
            for (var y = y0; y < y1; y++)
            {
                var row = image.GetPixelRowSpan(y);
                for (var x = x0; x < x1; x++) row[x] = paint;
            }
        }
    }

    /// <summary>Reads the finished file back: no text on boxed pages, the others unchanged, no term anywhere.</summary>
    private static void VerifyOutput(string path, List<PagePlan> plans, List<string> terms)
    {
        using var pig = PigDocument.Open(path);
        if (pig.NumberOfPages != plans.Count)
            throw new InvalidOperationException($"the redacted file has {pig.NumberOfPages} pages, expected {plans.Count}; nothing was written");
        for (var n = 1; n <= plans.Count; n++)
        {
            var page = pig.GetPage(n);
            var plan = plans[n - 1];
            if (plan.Boxes.Count > 0 && page.Letters.Count != 0)
                throw new InvalidOperationException($"page {n} still has extractable text after redaction; nothing was written");
            if (plan.Boxes.Count == 0 && page.Letters.Count != plan.LetterCount)
                throw new InvalidOperationException($"page {n} lost text while being copied; nothing was written");
            if (terms.Count > 0)
            {
                var text = string.Join(" ", page.GetWords().Select(w => w.Text));
                foreach (var term in terms)
                    if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException($"'{term}' is still in the text of page {n}; nothing was written");
            }
        }
    }
}
