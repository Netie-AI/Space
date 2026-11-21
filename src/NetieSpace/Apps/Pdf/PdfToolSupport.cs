using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// Shared plumbing for the PDF tool runners: option reading, page-range parsing, collision-free
/// output names and friendly open errors. Plain net8.0, no WPF.
/// </summary>
internal static class PdfToolSupport
{
    public static Task<IReadOnlyList<string>> Work(CancellationToken ct, Func<IReadOnlyList<string>> body) =>
        Task.Run(body, ct);

    public static string Opt(ToolRequest r, string key, string fallback = "") =>
        r.Options.TryGetValue(key, out var v) && v is not null ? v : fallback;

    public static bool OptBool(ToolRequest r, string key, bool fallback)
    {
        var v = Opt(r, key, "");
        if (v.Length == 0) return fallback;
        if (v == "true") return true;
        if (v == "false") return false;
        throw new ArgumentException($"option '{key}': '{v}' is not true or false");
    }

    public static int OptInt(ToolRequest r, string key, int fallback, int min, int max)
    {
        var v = Opt(r, key, "");
        if (v.Length == 0) return fallback;
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            throw new ArgumentException($"option '{key}': '{v}' is not an integer");
        if (n < min || n > max)
            throw new ArgumentException($"option '{key}': {n} is outside {min}..{max}");
        return n;
    }

    public static string OptChoice(ToolRequest r, string key, string fallback, params string[] allowed)
    {
        var v = Opt(r, key, fallback);
        var hit = allowed.FirstOrDefault(a => string.Equals(a, v, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            throw new ArgumentException($"option '{key}': '{v}' is not one of [{string.Join(", ", allowed)}]");
        return hit;
    }

    private static readonly Regex Token = new(@"^(-?\d+|end)(?:-(\d+|end))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Page-range text to 1-based page numbers, in the order written. Endpoints: N, "end", or -N
    /// (N-th page from the end, -1 = last). Throws naming <paramref name="key"/>.
    /// </summary>
    public static List<int> ParsePages(string spec, int pageCount, string key)
    {
        var result = new List<int>();
        foreach (var raw in SplitTokens(spec))
        {
            var (a, b) = ResolveToken(raw, pageCount, key);
            for (var p = a; p <= b; p++) result.Add(p);
        }
        if (result.Count == 0)
            throw new ArgumentException($"option '{key}': no pages given");
        return result;
    }

    public static IReadOnlyList<string> SplitTokens(string spec) =>
        (spec ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    public static (int First, int Last) ResolveToken(string token, int pageCount, string key)
    {
        var m = Token.Match(token);
        if (!m.Success)
            throw new ArgumentException($"option '{key}': '{token}' is not a page range");
        int End(string s)
        {
            if (string.Equals(s, "end", StringComparison.OrdinalIgnoreCase)) return pageCount;
            var n = int.Parse(s, CultureInfo.InvariantCulture);
            if (n < 0) n = pageCount + n + 1;
            return n;
        }
        var first = End(m.Groups[1].Value);
        var last = m.Groups[2].Success ? End(m.Groups[2].Value) : first;
        if (first < 1 || first > pageCount)
            throw new ArgumentException($"option '{key}': page {first} (from '{token}') is outside 1..{pageCount}");
        if (last < 1 || last > pageCount)
            throw new ArgumentException($"option '{key}': page {last} (from '{token}') is outside 1..{pageCount}");
        if (last < first)
            throw new ArgumentException($"option '{key}': '{token}' runs backwards");
        return (first, last);
    }

    /// <summary>Opens a PDF for page import, turning reader failures into a message that names the file.</summary>
    public static PdfDocument OpenImport(string path, string? password = null) => Open(path, password, PdfDocumentOpenMode.Import);

    public static PdfDocument OpenModify(string path, string? password = null) => Open(path, password, PdfDocumentOpenMode.Modify);

    private static PdfDocument Open(string path, string? password, PdfDocumentOpenMode mode)
    {
        PdfDocument doc;
        try
        {
            doc = password is null ? PdfReader.Open(path, mode) : PdfReader.Open(path, password, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException(
                $"cannot open {path}: {ex.Message} (password protected files need pdf-unlock first; damaged files need pdf-repair)", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"cannot read {path} as a PDF: {ex.Message}", ex);
        }
        if (doc.PageCount == 0)
        {
            doc.Dispose();
            throw new InvalidOperationException("PDF has no pages: " + path);
        }
        return doc;
    }

    /// <summary>
    /// Adds <paramref name="src"/> to <paramref name="dest"/> without the pages it links to. A link annotation with a
    /// /Dest or a /GoTo action points at another page object, and importing a page pulls in everything it points at,
    /// so a one-page extract would carry the whole linked-to chain of pages (and an unlock would double them). The
    /// links are dropped from the source page (read-only import copy, never saved) first; every other annotation,
    /// and links to a web address or a file, stay.
    /// </summary>
    public static PdfPage AddPageClean(PdfDocument dest, PdfPage src)
    {
        DropPageLinks(src);
        return dest.AddPage(src);
    }

    private static void DropPageLinks(PdfPage page)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return;
        var items = annots.Elements;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var d = (items[i] as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfDictionary ?? items[i] as PdfDictionary;
            if (d is not null && PointsAtAPage(d)) items.RemoveAt(i);
        }
        if (items.Count == 0) page.Elements.Remove("/Annots");
    }

    private static bool PointsAtAPage(PdfDictionary annot)
    {
        if (annot.Elements.ContainsKey("/Dest")) return true;
        foreach (var key in new[] { "/A", "/PA" })
        {
            var action = annot.Elements.GetDictionary(key);
            if (action is not null && string.Equals(action.Elements.GetName("/S"), "/GoTo", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Carries the descriptive document fields (title, author, subject, keywords) from <paramref name="from"/> to a
    /// document built page by page, so a split, merge or extract does not silently drop the title.
    /// </summary>
    public static void CopyInfo(PdfDocument from, PdfDocument to)
    {
        var i = from.Info;
        if (!string.IsNullOrEmpty(i.Title)) to.Info.Title = i.Title;
        if (!string.IsNullOrEmpty(i.Author)) to.Info.Author = i.Author;
        if (!string.IsNullOrEmpty(i.Subject)) to.Info.Subject = i.Subject;
        if (!string.IsNullOrEmpty(i.Keywords)) to.Info.Keywords = i.Keywords;
    }

    private static PdfRectangle Transposed(PdfRectangle r) =>
        new(new XPoint(r.X1, r.Y1), new XPoint(r.X1 + r.Height, r.Y1 + r.Width));

    /// <summary>
    /// Sets /Rotate on a page opened for editing so that the page boxes written to the file stay as they were. When a
    /// page that carries a quarter turn (90 or 270) is saved with a half turn or none, this engine writes its boxes
    /// transposed, which clips the content; handing it the transposed boxes up front cancels that out. The caller
    /// still reads the result back (see <see cref="PdfEditTools"/>).
    /// </summary>
    public static void SetRotate(PdfPage page, int degrees)
    {
        var wasQuarter = ((page.Rotate % 360) + 360) % 360 % 180 != 0;
        var willBeQuarter = degrees % 180 != 0;
        var media = page.MediaBox;
        var crop = page.CropBox;
        page.Rotate = degrees;
        if (wasQuarter && !willBeQuarter)
        {
            page.MediaBox = Transposed(media);
            if (!crop.IsEmpty) page.CropBox = Transposed(crop);
        }
    }

    public static string Stem(string path) => IoPath.GetFileNameWithoutExtension(path);

    /// <summary>
    /// Hands out output paths inside one output folder. Never returns the path of a file that already
    /// exists (so an input, or an earlier run, is never overwritten) nor one handed out earlier in the run.
    /// </summary>
    public sealed class OutputNamer
    {
        private readonly string _dir;
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

        public OutputNamer(string outDir) { _dir = outDir; }

        public string Next(string stem, string ext)
        {
            var safe = string.Concat(stem.Select(c => IoPath.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (safe.Length == 0) safe = "output";
            var candidate = IoPath.Combine(_dir, safe + ext);
            var n = 2;
            while (_used.Contains(candidate) || IoFile.Exists(candidate) || IoDirectory.Exists(candidate))
                candidate = IoPath.Combine(_dir, safe + "-" + n++ + ext);
            _used.Add(candidate);
            return candidate;
        }
    }

    public static void Report(IProgress<ToolProgress>? p, int done, int total, string what) =>
        p?.Report(new ToolProgress(Math.Min(99, done * 100 / Math.Max(1, total)), what));

    // ----- page geometry (visual box: crop box if present, then /Rotate applied) -----

    public static void EffectiveBox(PdfPage page, out double x0, out double y0, out double w, out double h)
    {
        var mb = page.MediaBox;
        var mx0 = Math.Min(mb.X1, mb.X2);
        var my0 = Math.Min(mb.Y1, mb.Y2);
        var mx1 = Math.Max(mb.X1, mb.X2);
        var my1 = Math.Max(mb.Y1, mb.Y2);
        x0 = mx0; y0 = my0; w = mx1 - mx0; h = my1 - my0;
        var cb = page.CropBox;
        if (cb.IsEmpty) return;
        var cx0 = Math.Max(mx0, Math.Min(cb.X1, cb.X2));
        var cy0 = Math.Max(my0, Math.Min(cb.Y1, cb.Y2));
        var cx1 = Math.Min(mx1, Math.Max(cb.X1, cb.X2));
        var cy1 = Math.Min(my1, Math.Max(cb.Y1, cb.Y2));
        if (cx1 - cx0 < 1 || cy1 - cy0 < 1) return;
        x0 = cx0; y0 = cy0; w = cx1 - cx0; h = cy1 - cy0;
    }

    /// <summary>Visual (as displayed) width and height of a page.</summary>
    public static (double W, double H) VisualSize(PdfPage page)
    {
        EffectiveBox(page, out _, out _, out var w, out var h);
        var rot = ((page.Rotate % 360) + 360) % 360;
        return rot is 90 or 270 ? (h, w) : (w, h);
    }

    /// <summary>
    /// Opens a drawing surface on <paramref name="page"/> whose origin is the top-left corner of the page as
    /// displayed (crop box and /Rotate honoured) and whose size is returned in visualW/visualH.
    /// </summary>
    public static XGraphics OpenVisualGraphics(PdfPage page, bool append, out double visualW, out double visualH)
    {
        EffectiveBox(page, out var bx, out var by, out var w, out var h);
        var gfx = XGraphics.FromPdfPage(page, append ? XGraphicsPdfPageOptions.Append : XGraphicsPdfPageOptions.Prepend);

        // Graphics space is y-down; PdfSharp flips y about page.Height, which for a page with /Rotate 90 or 270
        // is the media width. Move to the effective box's top-left in that space.
        var gfxHeight = page.Height.Point;
        var topLeftX = bx;
        var topLeftY = gfxHeight - (by + h);
        gfx.TranslateTransform(topLeftX, topLeftY);

        var rot = ((page.Rotate % 360) + 360) % 360;
        switch (rot)
        {
            case 90:
                gfx.TranslateTransform(0, h);
                gfx.RotateTransform(-90);
                visualW = h; visualH = w;
                break;
            case 180:
                gfx.TranslateTransform(w, h);
                gfx.RotateTransform(180);
                visualW = w; visualH = h;
                break;
            case 270:
                gfx.TranslateTransform(w, 0);
                gfx.RotateTransform(90);
                visualW = h; visualH = w;
                break;
            default:
                visualW = w; visualH = h;
                break;
        }
        return gfx;
    }

    public static XColor ParseColor(string text, string key, int opacityPercent = 100)
    {
        var m = Regex.Match(text ?? "", "^#([0-9a-fA-F]{2})?([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$");
        if (!m.Success)
            throw new ArgumentException($"option '{key}': '{text}' is not #RRGGBB or #AARRGGBB");
        var a = m.Groups[1].Success ? Convert.ToInt32(m.Groups[1].Value, 16) : 255;
        a = a * opacityPercent / 100;
        return XColor.FromArgb(a, Convert.ToInt32(m.Groups[2].Value, 16), Convert.ToInt32(m.Groups[3].Value, 16), Convert.ToInt32(m.Groups[4].Value, 16));
    }

    private static readonly string[] FontFamilies = { "Arial", "Helvetica", "Segoe UI", "DejaVu Sans", "Liberation Sans", "FreeSans" };

    public static XFont ResolveFont(double size, XFontStyle style = XFontStyle.Regular)
    {
        Exception? last = null;
        foreach (var f in FontFamilies)
        {
            try
            {
                return new XFont(f, size, style);
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("no usable font found for text drawing: " + last?.Message);
    }
}
