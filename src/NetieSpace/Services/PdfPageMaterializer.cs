using UglyToad.PdfPig;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>Materialize PDF pages locally for Cortex RAG / document_qa (Cortex has no PDF ingest).</summary>
public static class PdfPageMaterializer
{
    public sealed record PageChunk(int Page, string Text);

    public static List<PageChunk> ExtractPages(string pdfPath, int maxPages = 80, int maxCharsPerPage = 4_000)
    {
        var list = new List<PageChunk>();
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var n = 0;
            foreach (var page in doc.GetPages())
            {
                n++;
                if (n > maxPages) break;
                var text = (page.Text ?? "").Trim();
                if (text.Length > maxCharsPerPage)
                    text = text[..maxCharsPerPage] + "\n…(truncated)";
                list.Add(new PageChunk(n, text));
            }
        }
        catch
        {
            // Fall through empty
        }

        return list;
    }

    public static List<(string Id, string Text, int? Page)> ToRagCorpus(
        IReadOnlyList<PageChunk> pages,
        string? question = null,
        int maxChunks = 16)
    {
        if (pages.Count == 0)
            return [];

        IEnumerable<PageChunk> ordered = pages;
        if (!string.IsNullOrWhiteSpace(question))
        {
            var tokens = question.ToLowerInvariant()
                .Split([' ', '\t', '\n', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .Distinct()
                .Take(12)
                .ToList();
            ordered = pages
                .Select(p => (p, score: tokens.Count(t => p.Text.Contains(t, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.p.Page)
                .Select(x => x.p);
        }

        // Always include first + last
        var picked = new List<PageChunk>();
        void Add(PageChunk p)
        {
            if (picked.Any(x => x.Page == p.Page)) return;
            if (picked.Count >= maxChunks) return;
            picked.Add(p);
        }

        if (pages.Count > 0) Add(pages[0]);
        foreach (var p in ordered)
            Add(p);
        if (pages.Count > 1) Add(pages[^1]);

        return picked
            .OrderBy(p => p.Page)
            .Select(p => ($"p{p.Page}", $"--- page {p.Page} ---\n{p.Text}", (int?)p.Page))
            .ToList();
    }

    public static bool ShouldEscalateToCortex(PreviewContent content, string userMessage)
    {
        var rawLen = content.RawText?.Length ?? 0;
        if (rawLen > DocumentContextPacker.HugeThreshold)
            return true;

        var q = userMessage.ToLowerInvariant();
        if (q.Contains("whole report") || q.Contains("across") || q.Contains("compare section") ||
            q.Contains("all pages") || q.Contains("entire document") || q.Contains("every page"))
            return true;

        if (content.Kind == PreviewKind.Pdf &&
            (q.Contains("figure") || q.Contains("image") || q.Contains("chart") || q.Contains("diagram")))
            return true;

        return false;
    }
}
