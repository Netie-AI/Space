using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Pdf;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>Page-level tools on PdfSharpCore: merge, split, remove, extract, organize.</summary>
internal static class PdfOrganizeTools
{
    // ----- pdf-merge -----

    public static Task<IReadOnlyList<string>> MergeAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            if (req.Files.Count < 2)
                throw new ArgumentException("pdf-merge needs at least two PDFs, got " + req.Files.Count);
            using var outDoc = new PdfDocument();
            for (var i = 0; i < req.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var src = OpenImport(req.Files[i]);
                for (var p = 0; p < src.PageCount; p++) outDoc.AddPage(src.Pages[p]);
                Report(progress, i + 1, req.Files.Count, "merged " + IoPath.GetFileName(req.Files[i]));
            }
            var path = new OutputNamer(req.OutDir).Next(Stem(req.Files[0]) + "_merged", ".pdf");
            outDoc.Save(path);
            return new[] { path };
        });

    // ----- pdf-split -----

    public static Task<IReadOnlyList<string>> SplitAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var mode = OptChoice(req, "split_mode", "ranges", "ranges", "fixed_range", "remove_pages", "filesize");
            var input = req.Files[0];
            var names = new OutputNamer(req.OutDir);
            using var src = OpenImport(input);
            var stem = Stem(input);
            var outputs = new List<string>();

            void WritePart(IEnumerable<int> pages0, int index)
            {
                using var d = new PdfDocument();
                foreach (var p in pages0) d.AddPage(src.Pages[p]);
                var path = names.Next($"{stem}_part{index:D3}", ".pdf");
                d.Save(path);
                outputs.Add(path);
            }

            switch (mode)
            {
                case "ranges":
                {
                    var tokens = SplitTokens(Opt(req, "ranges"));
                    if (tokens.Count == 0)
                        throw new ArgumentException("option 'ranges': give at least one range for split_mode=ranges, for example 1-3,4-end");
                    var n = 0;
                    foreach (var t in tokens)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (a, b) = ResolveToken(t, src.PageCount, "ranges");
                        WritePart(Enumerable.Range(a - 1, b - a + 1), ++n);
                        Report(progress, n, tokens.Count, "wrote part " + n);
                    }
                    break;
                }
                case "fixed_range":
                {
                    var every = OptInt(req, "every", 1, 1, int.MaxValue);
                    var parts = (src.PageCount + every - 1) / every;
                    for (var i = 0; i < parts; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var first = i * every;
                        WritePart(Enumerable.Range(first, Math.Min(every, src.PageCount - first)), i + 1);
                        Report(progress, i + 1, parts, "wrote part " + (i + 1));
                    }
                    break;
                }
                case "remove_pages":
                {
                    var drop = new HashSet<int>(ParsePages(Opt(req, "ranges"), src.PageCount, "ranges"));
                    var keep = Enumerable.Range(1, src.PageCount).Where(p => !drop.Contains(p)).Select(p => p - 1).ToList();
                    if (keep.Count == 0) throw new ArgumentException("option 'ranges': that removes every page");
                    var path = names.Next(stem + "_removed", ".pdf");
                    using var d = new PdfDocument();
                    foreach (var p in keep) d.AddPage(src.Pages[p]);
                    d.Save(path);
                    outputs.Add(path);
                    break;
                }
                case "filesize":
                {
                    var limitKb = OptInt(req, "max_size_kb", 0, 0, int.MaxValue);
                    if (limitKb < 1) throw new ArgumentException("option 'max_size_kb': give a size limit in KB for split_mode=filesize");
                    var limit = (long)limitKb * 1024;
                    var chunk = new List<int>();
                    var index = 0;
                    for (var p = 0; p < src.PageCount; p++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var trial = new List<int>(chunk) { p };
                        var size = MeasureSaved(src, trial);
                        if (size > limit)
                        {
                            if (chunk.Count == 0)
                                throw new InvalidOperationException(
                                    $"page {p + 1} alone is {size / 1024} KB, above max_size_kb={limitKb}; raise the limit");
                            WritePart(chunk, ++index);
                            chunk = new List<int> { p };
                            var single = MeasureSaved(src, chunk);
                            if (single > limit)
                                throw new InvalidOperationException(
                                    $"page {p + 1} alone is {single / 1024} KB, above max_size_kb={limitKb}; raise the limit");
                        }
                        else chunk = trial;
                        Report(progress, p + 1, src.PageCount, "page " + (p + 1));
                    }
                    if (chunk.Count > 0) WritePart(chunk, ++index);
                    break;
                }
            }
            return outputs;
        });

    private static long MeasureSaved(PdfDocument src, List<int> pages0)
    {
        using var d = new PdfDocument();
        foreach (var p in pages0) d.AddPage(src.Pages[p]);
        using var ms = new MemoryStream();
        d.Save(ms, false);
        return ms.Length;
    }

    // ----- pdf-remove-pages -----

    public static Task<IReadOnlyList<string>> RemovePagesAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            using var src = OpenImport(req.Files[0]);
            var spec = Opt(req, "pages");
            if (spec.Length == 0) throw new ArgumentException("option 'pages': say which pages to remove, for example 2,5-7");
            var drop = new HashSet<int>(ParsePages(spec, src.PageCount, "pages"));
            if (drop.Count >= src.PageCount) throw new ArgumentException("option 'pages': that removes every page");
            using var d = new PdfDocument();
            for (var p = 0; p < src.PageCount; p++)
            {
                ct.ThrowIfCancellationRequested();
                if (!drop.Contains(p + 1)) d.AddPage(src.Pages[p]);
            }
            var path = new OutputNamer(req.OutDir).Next(Stem(req.Files[0]) + "_removed", ".pdf");
            d.Save(path);
            return new[] { path };
        });

    // ----- pdf-extract-pages -----

    public static Task<IReadOnlyList<string>> ExtractPagesAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            using var src = OpenImport(req.Files[0]);
            var spec = Opt(req, "pages");
            if (spec.Length == 0) throw new ArgumentException("option 'pages': say which pages to extract, for example 1-3,8");
            var pages = ParsePages(spec, src.PageCount, "pages");
            var merge = OptBool(req, "merge", true);
            var names = new OutputNamer(req.OutDir);
            var stem = Stem(req.Files[0]);
            var outputs = new List<string>();
            if (merge)
            {
                using var d = new PdfDocument();
                foreach (var p in pages) d.AddPage(src.Pages[p - 1]);
                var path = names.Next(stem + "_extract", ".pdf");
                d.Save(path);
                outputs.Add(path);
            }
            else
            {
                var i = 0;
                foreach (var p in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    using var d = new PdfDocument();
                    d.AddPage(src.Pages[p - 1]);
                    var path = names.Next($"{stem}_page-{p:D3}", ".pdf");
                    d.Save(path);
                    outputs.Add(path);
                    Report(progress, ++i, pages.Count, "page " + p);
                }
            }
            return outputs;
        });

    // ----- pdf-organize -----

    /// <summary>
    /// plan = items separated by ';'. An item is "blank" or "FILE:PAGES" or "FILE:PAGES@DEG" where FILE is the
    /// 1-based position among the input files, PAGES is a page range list and DEG adds 90, 180 or 270 degrees.
    /// An empty plan keeps every page of every file in order.
    /// </summary>
    public static Task<IReadOnlyList<string>> OrganizeAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var docs = new List<PdfDocument>();
            try
            {
                foreach (var f in req.Files) docs.Add(OpenImport(f));
                var plan = Opt(req, "plan").Trim();
                if (plan.Length == 0)
                    plan = string.Join(";", Enumerable.Range(1, docs.Count).Select(i => i + ":1-end"));

                using var outDoc = new PdfDocument();
                double lastW = 595.28, lastH = 841.89;
                var items = plan.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var n = 0;
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(item, "blank", StringComparison.OrdinalIgnoreCase))
                    {
                        var bp = outDoc.AddPage();
                        bp.Width = lastW; bp.Height = lastH;
                    }
                    else
                    {
                        var rot = 0;
                        var body = item;
                        var at = item.LastIndexOf('@');
                        if (at >= 0)
                        {
                            body = item.Substring(0, at);
                            if (!int.TryParse(item.Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out rot) || rot is not (0 or 90 or 180 or 270))
                                throw new ArgumentException($"option 'plan': rotation in '{item}' must be 0, 90, 180 or 270");
                        }
                        var colon = body.IndexOf(':');
                        if (colon <= 0 || !int.TryParse(body.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileNo))
                            throw new ArgumentException($"option 'plan': '{item}' is not blank or FILE:PAGES[@DEG]");
                        if (fileNo < 1 || fileNo > docs.Count)
                            throw new ArgumentException($"option 'plan': file {fileNo} in '{item}' is outside 1..{docs.Count}");
                        var src = docs[fileNo - 1];
                        foreach (var p in ParsePages(body.Substring(colon + 1), src.PageCount, "plan"))
                        {
                            var page = outDoc.AddPage(src.Pages[p - 1]);
                            if (rot != 0) page.Rotate = (page.Rotate + rot) % 360;
                            lastW = page.Width.Point; lastH = page.Height.Point;
                        }
                    }
                    Report(progress, ++n, items.Count, "plan step " + n);
                }
                if (outDoc.PageCount == 0) throw new ArgumentException("option 'plan': produces no pages");
                var path = new OutputNamer(req.OutDir).Next(Stem(req.Files[0]) + "_organized", ".pdf");
                outDoc.Save(path);
                return new[] { path };
            }
            finally
            {
                foreach (var d in docs) d.Dispose();
            }
        });
}
