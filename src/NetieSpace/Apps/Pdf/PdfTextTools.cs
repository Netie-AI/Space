using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>Text-reading tools on PdfPig: compare and to-markdown.</summary>
internal static class PdfTextTools
{
    internal sealed record TextLine(int Page, string Text, double FontSize, double Left, double Baseline, bool Bold);

    /// <summary>Reading-order lines of one page: words grouped by baseline, left to right.</summary>
    internal static List<TextLine> PageLines(Page page, int pageNo)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var lines = new List<List<Word>>();
        foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var h = Math.Max(1.0, w.BoundingBox.Height);
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - w.BoundingBox.Bottom) < h * 0.5);
            if (line is null) lines.Add(new List<Word> { w });
            else line.Add(w);
        }
        return lines
            .OrderByDescending(l => l.Average(w => w.BoundingBox.Bottom))
            .Select(l =>
            {
                var ordered = l.OrderBy(w => w.BoundingBox.Left).ToList();
                var letters = ordered.SelectMany(w => w.Letters).ToList();
                var size = letters.Count == 0 ? 10 : letters.Average(x => x.PointSize);
                var bold = letters.Count > 0 && letters.Count(x => x.FontName != null && x.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)) * 2 > letters.Count;
                return new TextLine(pageNo, string.Join(" ", ordered.Select(w => w.Text)), size,
                    ordered[0].BoundingBox.Left, ordered[0].BoundingBox.Bottom, bold);
            })
            .ToList();
    }

    private static List<TextLine> ReadAll(string path, CancellationToken ct)
    {
        var result = new List<TextLine>();
        try
        {
            using var doc = PdfDocument.Open(path);
            for (var n = 1; n <= doc.NumberOfPages; n++)
            {
                ct.ThrowIfCancellationRequested();
                result.AddRange(PageLines(doc.GetPage(n), n));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"cannot read {path} as a PDF: {ex.Message}", ex);
        }
        return result;
    }

    // ----- pdf-compare -----

    public static Task<IReadOnlyList<string>> CompareAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            if (req.Files.Count != 2) throw new ArgumentException("pdf-compare needs exactly two PDFs");
            var a = ReadAll(req.Files[0], ct);
            progress?.Report(new ToolProgress(30, "read first file"));
            var b = ReadAll(req.Files[1], ct);
            progress?.Report(new ToolProgress(60, "read second file"));
            if (a.Count == 0 && b.Count == 0)
                throw new InvalidOperationException("neither file has an extractable text layer (scanned PDFs need pdf-ocr first)");

            var ops = Diff(a.Select(l => Norm(l.Text)).ToList(), b.Select(l => Norm(l.Text)).ToList(), ct);
            var sb = new StringBuilder();
            var nameA = IoPath.GetFileName(req.Files[0]);
            var nameB = IoPath.GetFileName(req.Files[1]);
            var removed = ops.Count(o => o.Kind == '-');
            var added = ops.Count(o => o.Kind == '+');
            sb.AppendLine("# Text comparison");
            sb.AppendLine();
            sb.AppendLine("- Before: " + nameA + " (" + a.Select(l => l.Page).Distinct().Count() + " pages with text)");
            sb.AppendLine("- After: " + nameB + " (" + b.Select(l => l.Page).Distinct().Count() + " pages with text)");
            sb.AppendLine("- Lines removed: " + removed);
            sb.AppendLine("- Lines added: " + added);
            sb.AppendLine();
            if (removed + added == 0)
                sb.AppendLine("The two files have identical text.");
            else
            {
                sb.AppendLine("## Changes");
                sb.AppendLine();
                foreach (var o in ops.Where(o => o.Kind != '='))
                {
                    var line = o.Kind == '-' ? a[o.Index] : b[o.Index];
                    sb.AppendLine($"{o.Kind} (page {line.Page}) {line.Text}");
                }
            }

            var path = new OutputNamer(req.OutDir).Next(Stem(req.Files[0]) + "_vs_" + Stem(req.Files[1]) + "_compare", ".md");
            IoFile.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return new[] { path };
        });

    private static string Norm(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal readonly record struct DiffOp(char Kind, int Index);

    /// <summary>Myers O(ND) line diff. Index refers to the list the line came from (a for '-' and '=', b for '+').</summary>
    internal static List<DiffOp> Diff(List<string> a, List<string> b, CancellationToken ct)
    {
        int n = a.Count, m = b.Count, max = n + m;
        var ops = new List<DiffOp>();
        if (max == 0) return ops;
        var v = new int[2 * max + 2];
        var trace = new List<int[]>();
        var found = false;
        for (var d = 0; d <= max && !found; d++)
        {
            ct.ThrowIfCancellationRequested();
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[k - 1 + max] < v[k + 1 + max])) x = v[k + 1 + max];
                else x = v[k - 1 + max] + 1;
                var y = x - k;
                while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                v[k + max] = x;
                if (x >= n && y >= m) { found = true; break; }
            }
        }

        // Backtrack.
        var xx = n; var yy = m;
        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var vv = trace[d];
            var k = xx - yy;
            int prevK;
            if (k == -d || (k != d && vv[k - 1 + max] < vv[k + 1 + max])) prevK = k + 1; else prevK = k - 1;
            var prevX = vv[prevK + max];
            var prevY = prevX - prevK;
            while (xx > prevX && yy > prevY) { xx--; yy--; ops.Add(new DiffOp('=', xx)); }
            if (d > 0)
            {
                if (xx == prevX) { yy--; ops.Add(new DiffOp('+', yy)); }
                else { xx--; ops.Add(new DiffOp('-', xx)); }
            }
        }
        ops.Reverse();
        return ops;
    }

    // ----- pdf-to-markdown -----

    public static Task<IReadOnlyList<string>> ToMarkdownAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                ct.ThrowIfCancellationRequested();
                var input = req.Files[f];
                var lines = ReadAll(input, ct);
                if (lines.Count == 0)
                    throw new InvalidOperationException($"no extractable text layer in {input} (scanned PDF? use pdf-ocr)");
                var md = Render(lines);
                var path = names.Next(Stem(input), ".md");
                IoFile.WriteAllText(path, md, new UTF8Encoding(false));
                outputs.Add(path);
                Report(progress, f + 1, req.Files.Count, "converted " + IoPath.GetFileName(input));
            }
            return outputs;
        });

    /// <summary>
    /// Heuristic layout to Markdown: the most common line size is body text; clearly larger lines become
    /// headings (bigger = higher level); bullet and numbered lines become list items; a vertical gap
    /// larger than a normal line ends a paragraph. No table detection.
    /// </summary>
    internal static string Render(List<TextLine> lines)
    {
        var body = lines.GroupBy(l => Math.Round(l.FontSize * 2) / 2).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
        var headingSizes = lines.Where(l => l.FontSize > body * 1.12 && l.Text.Length <= 140)
            .Select(l => Math.Round(l.FontSize * 2) / 2).Distinct().OrderByDescending(x => x).Take(3).ToList();

        var sb = new StringBuilder();
        var paragraph = new StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0) return;
            sb.AppendLine(paragraph.ToString().Trim());
            sb.AppendLine();
            paragraph.Clear();
        }

        TextLine? prev = null;
        foreach (var l in lines)
        {
            var size = Math.Round(l.FontSize * 2) / 2;
            var text = l.Text;
            var pageBreak = prev is not null && prev.Page != l.Page;
            var gap = prev is null || pageBreak ? 0 : prev.Baseline - l.Baseline;
            var bigGap = prev is not null && !pageBreak && gap > body * 1.9;

            var level = headingSizes.IndexOf(size);
            if (level >= 0 && size > body * 1.12)
            {
                Flush();
                sb.AppendLine(new string('#', level + 1) + " " + text);
                sb.AppendLine();
            }
            else if (IsBullet(text, out var bulletText))
            {
                Flush();
                sb.AppendLine("- " + bulletText);
            }
            else if (IsNumbered(text, out var num, out var rest))
            {
                Flush();
                sb.AppendLine(num + ". " + rest);
            }
            else
            {
                if (bigGap) Flush();
                if (paragraph.Length > 0) paragraph.Append(' ');
                paragraph.Append(text);
            }
            prev = l;
        }
        Flush();
        return sb.ToString().TrimEnd() + "\n";
    }

    private static bool IsBullet(string t, out string rest)
    {
        rest = "";
        if (t.Length > 2 && (t[0] == '\u2022' || t[0] == '-' || t[0] == '*' || t[0] == '\u25CF') && t[1] == ' ')
        {
            rest = t.Substring(2).Trim();
            return rest.Length > 0;
        }
        return false;
    }

    private static bool IsNumbered(string t, out string num, out string rest)
    {
        num = ""; rest = "";
        var i = 0;
        while (i < t.Length && char.IsDigit(t[i])) i++;
        if (i == 0 || i > 3 || i + 1 >= t.Length || (t[i] != '.' && t[i] != ')') || t[i + 1] != ' ') return false;
        num = t.Substring(0, i);
        rest = t.Substring(i + 2).Trim();
        return rest.Length > 0;
    }
}
