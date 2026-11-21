using System;
using System.Globalization;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// Where a click on a page preview puts the signature: the centre of the mark as a percentage of the page's
/// width and height (0,0 = top left). The "at" option of pdf-sign-self carries it as "62.5,88".
/// </summary>
public static class PdfSignPlacement
{
    public static string Format(double xPercent, double yPercent)
    {
        var inv = CultureInfo.InvariantCulture;
        return Math.Round(Math.Clamp(xPercent, 0, 100), 1).ToString("0.#", inv) + "," + Math.Round(Math.Clamp(yPercent, 0, 100), 1).ToString("0.#", inv);
    }

    /// <summary>Percentages from a click at (<paramref name="x"/>, <paramref name="y"/>) on a preview of the given size.</summary>
    public static string FromClick(double x, double y, double previewWidth, double previewHeight) =>
        previewWidth <= 0 || previewHeight <= 0 ? Format(50, 50) : Format(x / previewWidth * 100, y / previewHeight * 100);

    public static bool TryParse(string? text, out double xPercent, out double yPercent)
    {
        xPercent = yPercent = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',');
        if (parts.Length != 2) return false;
        var inv = CultureInfo.InvariantCulture;
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, inv, out xPercent)) return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, inv, out yPercent)) return false;
        if (double.IsNaN(xPercent) || double.IsNaN(yPercent) || double.IsInfinity(xPercent) || double.IsInfinity(yPercent)) return false;
        return xPercent is >= 0 and <= 100 && yPercent is >= 0 and <= 100;
    }

    /// <summary>
    /// The top left of a block of <paramref name="blockW"/> x <paramref name="blockH"/> points centred on the click and
    /// kept <paramref name="edge"/> points inside the page; a page too small for that centres the block.
    /// </summary>
    public static (double Left, double Top) BlockOrigin(double xPercent, double yPercent, double pageW, double pageH, double blockW, double blockH, double edge)
    {
        static double Fit(double centre, double size, double page, double edge)
        {
            var lo = edge;
            var hi = page - edge - size;
            if (hi < lo) return (page - size) / 2;
            return Math.Clamp(centre - size / 2, lo, hi);
        }
        return (Fit(pageW * xPercent / 100, blockW, pageW, edge), Fit(pageH * yPercent / 100, blockH, pageH, edge));
    }
}

/// <summary>Which files a drop may add to the PDF tools: existing .pdf files; everything else is named back.</summary>
public static class PdfDropRules
{
    public sealed record Result(System.Collections.Generic.IReadOnlyList<string> Accepted, System.Collections.Generic.IReadOnlyList<string> Skipped)
    {
        /// <summary>"2 files skipped: notes.txt, scan.jpg" or null.</summary>
        public string? Message => Skipped.Count == 0
            ? null
            : (Skipped.Count == 1 ? "1 file skipped (not a PDF): " : Skipped.Count + " files skipped (not PDFs): ") + string.Join(", ", System.Linq.Enumerable.Take(Skipped, 3)) + (Skipped.Count > 3 ? ", ..." : "");
    }

    public static Result Filter(System.Collections.Generic.IEnumerable<string> dropped, Func<string, bool> exists)
    {
        var ok = new System.Collections.Generic.List<string>();
        var no = new System.Collections.Generic.List<string>();
        foreach (var f in dropped)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            var isPdf = string.Equals(System.IO.Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase);
            if (isPdf && exists(f)) ok.Add(f);
            else no.Add(System.IO.Path.GetFileName(f.TrimEnd('\\', '/')));
        }
        return new Result(ok, no);
    }
}
