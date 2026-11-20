using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using static NetieSpace.Apps.Pdf.PdfToolSupport;
using Pig = UglyToad.PdfPig;
using PigWriter = UglyToad.PdfPig.Writer;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// pdf-repair: PdfSharpCore refuses a file whose cross-reference table or trailer is damaged. PdfPig reads such a
/// file leniently (it searches the body for objects), so the pages it can read are copied one by one into a new
/// document written by PdfPig's own writer. Nothing is rebuilt from guesses: a page is either copied from what the
/// reader found or reported as unreadable. Not carried over: bookmarks, form fields, attachments, links and other
/// annotations, tagged structure, page labels, XMP metadata (title, author, subject, keywords are kept).
/// </summary>
internal static class PdfRepairTool
{
    public static Task<IReadOnlyList<string>> RunAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var partial = OptBool(req, "partial", false);
        return PdfTool3Batch.Run(req, progress, ct, "_repaired", ".pdf", (input, path, c) => Repair(input, path, partial, c));
    }

    private static void Repair(string input, string path, bool partial, CancellationToken ct)
    {
        if (OpensInPdfSharp(input))
            throw new InvalidOperationException("nothing to repair: the file already opens without errors (pdf-repair needs a damaged file)");

        byte[] bytes;
        try { bytes = IoFile.ReadAllBytes(input); }
        catch (Exception ex) { throw new InvalidOperationException("cannot read the file: " + ex.Message, ex); }

        var src = OpenLenient(bytes);

        using (src)
        {
            int total;
            try { total = src.NumberOfPages; }
            catch (Exception ex) { throw new InvalidOperationException("nothing could be recovered: the page tree is unreadable (" + ex.Message + ")", ex); }
            if (total < 1)
                throw new InvalidOperationException("nothing could be recovered: no pages were found");

            var builder = new PigWriter.PdfDocumentBuilder();
            var kept = new List<int>();
            var lost = new List<int>();
            var texts = new List<string>();
            var withContent = 0;
            for (var n = 1; n <= total; n++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var page = src.GetPage(n);
                    var text = Norm(page.Text);
                    // A page that shows nothing cannot be told from a page whose content was lost in the damage;
                    // it is reported instead of being copied as a blank page.
                    if (text.Length == 0 && page.NumberOfImages == 0 && page.Operations.Count == 0)
                    {
                        lost.Add(n);
                        continue;
                    }
                    builder.AddPage(src, n);
                    kept.Add(n);
                    texts.Add(text);
                    withContent++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    lost.Add(n);
                }
            }

            if (kept.Count == 0)
                throw new InvalidOperationException("nothing could be recovered: none of the " + total + " page(s) could be read");
            if (lost.Count > 0 && !partial)
                throw new InvalidOperationException(
                    $"page(s) {Ranges(lost)} of {total} could not be read or show nothing; nothing was written (set 'partial' to keep the {kept.Count} other page(s))");

            CopyDocumentInfo(src, builder);
            var output = builder.Build();
            try
            {
                IoFile.WriteAllBytes(path, output);
                ReadBack(path, texts);
            }
            catch
            {
                try { if (IoFile.Exists(path)) IoFile.Delete(path); } catch { /* best effort */ }
                throw;
            }
        }
    }

    private static Pig.PdfDocument OpenLenient(byte[] bytes)
    {
        var options = new Pig.ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true };
        Exception first;
        try
        {
            var d = Pig.PdfDocument.Open(bytes, options);
            if (d.NumberOfPages > 0) return d;
            d.Dispose();
            first = new InvalidOperationException("no pages were found");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            first = ex;
        }
        if (first.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("the file is password protected; run pdf-unlock first", first);

        // No usable cross-reference data or trailer: find the catalog by scanning for objects, give the reader a
        // trailer that names it, and let the reader's own object scan do the rest.
        var patched = WithScannedTrailer(bytes);
        if (patched is not null)
        {
            try
            {
                var d = Pig.PdfDocument.Open(patched, options);
                if (d.NumberOfPages > 0) return d;
                d.Dispose();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                first = ex;
            }
        }
        throw new InvalidOperationException("nothing could be recovered: no readable page structure was found (" + first.Message + ")", first);
    }

    private static readonly Regex ObjHeader = new(@"(?<![0-9])(\d{1,9})[ \t\r\n]+(\d{1,5})[ \t\r\n]+obj(?![a-z])", RegexOptions.Compiled);
    private static readonly Regex CatalogKey = new(@"/Type[ \t\r\n]*/Catalog(?![a-zA-Z])", RegexOptions.Compiled);
    private static readonly Regex InfoKey = new(@"/(Producer|Author|Title|Creator|CreationDate)\b", RegexOptions.Compiled);

    /// <summary>The bytes with a trailer appended that points at the last catalog object found in the body, or null.</summary>
    private static byte[]? WithScannedTrailer(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var headers = ObjHeader.Matches(text);
        if (headers.Count == 0) return null;
        long maxNum = 0;
        int? root = null, info = null;
        for (var i = 0; i < headers.Count; i++)
        {
            var num = int.Parse(headers[i].Groups[1].Value, CultureInfo.InvariantCulture);
            maxNum = Math.Max(maxNum, num);
            var start = headers[i].Index + headers[i].Length;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            var body = text.AsSpan(start, end - start);
            // Stream payloads can hold anything; only look at the dictionary in front of them.
            var streamAt = body.IndexOf("stream", StringComparison.Ordinal);
            var dict = (streamAt >= 0 ? body[..streamAt] : body).ToString();
            if (CatalogKey.IsMatch(dict) && dict.Contains("/Pages", StringComparison.Ordinal)) root = num;
            else if (!dict.Contains("/Type", StringComparison.Ordinal) && InfoKey.IsMatch(dict)) info = num;
        }
        if (root is null) return null;
        // The reader needs a startxref that lands on a real table; a table with only the free entry sends it to its
        // own scan of the body for every other object.
        var head = "\nxref\n0 1\n0000000000 65535 f \n";
        var trailer = $"trailer\n<< /Size {maxNum + 1} /Root {root} 0 R" + (info is null ? "" : $" /Info {info} 0 R") + " >>\nstartxref\n";
        var xrefAt = bytes.Length + 1;
        var add = Encoding.ASCII.GetBytes(head + trailer + xrefAt.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        var result = new byte[bytes.Length + add.Length];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        Buffer.BlockCopy(add, 0, result, bytes.Length, add.Length);
        return result;
    }

    private static void CopyDocumentInfo(Pig.PdfDocument src, PigWriter.PdfDocumentBuilder builder)
    {
        try
        {
            var i = src.Information;
            if (!string.IsNullOrEmpty(i.Title)) builder.DocumentInformation.Title = i.Title;
            if (!string.IsNullOrEmpty(i.Author)) builder.DocumentInformation.Author = i.Author;
            if (!string.IsNullOrEmpty(i.Subject)) builder.DocumentInformation.Subject = i.Subject;
            if (!string.IsNullOrEmpty(i.Keywords)) builder.DocumentInformation.Keywords = i.Keywords;
        }
        catch (Exception)
        {
            // The info dictionary is the thing most often damaged; losing it must not lose the pages.
        }
    }

    /// <summary>The repaired file must open in both readers with the pages and page text that were copied.</summary>
    private static void ReadBack(string path, List<string> expectedText)
    {
        try
        {
            using var sharp = OpenImport(path);
            if (sharp.PageCount != expectedText.Count)
                throw new InvalidOperationException($"the repaired copy has {sharp.PageCount} page(s), expected {expectedText.Count}");
            using var pig = Pig.PdfDocument.Open(path);
            if (pig.NumberOfPages != expectedText.Count)
                throw new InvalidOperationException($"the repaired copy has {pig.NumberOfPages} page(s), expected {expectedText.Count}");
            for (var i = 0; i < expectedText.Count; i++)
                if (Norm(pig.GetPage(i + 1).Text) != expectedText[i])
                    throw new InvalidOperationException($"the text of page {i + 1} changed in the repaired copy");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("the repaired copy") || ex.Message.StartsWith("the text of page"))
        {
            throw new InvalidOperationException(ex.Message + "; the repaired copy was discarded", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("the repaired copy does not open: " + ex.Message + "; it was discarded", ex);
        }
    }

    private static bool OpensInPdfSharp(string path)
    {
        try
        {
            using var d = PdfSharpCore.Pdf.IO.PdfReader.Open(path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
            var n = d.PageCount;
            for (var i = 0; i < n; i++) _ = d.Pages[i].MediaBox;
            return n > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Norm(string s) => string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Ranges(List<int> pages) => string.Join(", ", pages);
}
