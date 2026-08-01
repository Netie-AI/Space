using System.Text;
using System.Text.RegularExpressions;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// Section-aware packing so huge Add-to-Chat payloads stay within free-tier TPM budgets
/// without naive middle-omit destroying Excel/PDF locality.
/// </summary>
public static class DocumentContextPacker
{
    public const int DefaultBudget = 8_000;
    public const int HugeThreshold = 12_000;

    public sealed record PackResult(string Text, int OriginalChars, int PackedChars, string Detail);

    public static PackResult Pack(PreviewContent content, string? question = null, int budget = DefaultBudget)
    {
        var raw = content.RawText?.Trim() ?? "";
        if (raw.Length == 0)
            return new PackResult("(no extractable text)", 0, 0, "empty");

        if (raw.Length <= budget)
            return new PackResult(raw, raw.Length, raw.Length, $"full · {raw.Length:N0} chars");

        return content.Kind switch
        {
            PreviewKind.Pdf => PackPdf(raw, question, budget),
            PreviewKind.Office when LooksLikeWorkbook(raw) => PackWorkbook(raw, question, budget),
            PreviewKind.Image => PackGeneric(raw, budget, "image caption"),
            _ => PackGeneric(raw, budget, "digest")
        };
    }

    public static PackResult PackRaw(string? raw, PreviewKind kind, string? question = null, int budget = DefaultBudget)
    {
        var content = new PreviewContent
        {
            FilePath = "",
            FileName = "",
            Kind = kind,
            RawText = raw
        };
        return Pack(content, question, budget);
    }

    private static bool LooksLikeWorkbook(string raw) =>
        raw.Contains("Workbook:", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("Sheets (", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(raw, @"^##\s+\S+", RegexOptions.Multiline);

    private static PackResult PackPdf(string raw, string? question, int budget)
    {
        // Prefer page markers if present; else keyword windows + head/tail.
        var pages = SplitPages(raw);
        if (pages.Count <= 1)
            return PackGeneric(raw, budget, "pdf excerpt");

        var sb = new StringBuilder();
        var used = new HashSet<int>();
        void Take(int idx, string label)
        {
            if (idx < 0 || idx >= pages.Count || !used.Add(idx)) return;
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"--- page {idx + 1} ({label}) ---");
            sb.AppendLine(TrimBlock(pages[idx], Math.Min(2_400, budget / 3)));
        }

        Take(0, "first");
        if (!string.IsNullOrWhiteSpace(question))
        {
            var tokens = Tokenize(question);
            for (var i = 0; i < pages.Count && sb.Length < budget * 2 / 3; i++)
            {
                if (used.Contains(i)) continue;
                var score = tokens.Count(t => pages[i].Contains(t, StringComparison.OrdinalIgnoreCase));
                if (score > 0)
                    Take(i, "match");
            }
        }

        Take(pages.Count - 1, "last");

        // Fill remaining budget with early unread pages
        for (var i = 1; i < pages.Count - 1 && sb.Length < budget; i++)
            Take(i, "fill");

        var packed = TrimBlock(sb.ToString(), budget);
        return new PackResult(packed, raw.Length, packed.Length,
            $"pdf packed · {packed.Length:N0} of {raw.Length:N0} · {used.Count}/{pages.Count} pages");
    }

    private static PackResult PackWorkbook(string raw, string? question, int budget)
    {
        // Keep sheet headers + tables; prefer sheet named in question.
        var sheets = Regex.Split(raw, @"(?=^##\s+)", RegexOptions.Multiline)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sheets.Count == 0)
            return PackGeneric(raw, budget, "workbook");

        var header = sheets[0].StartsWith("Workbook", StringComparison.OrdinalIgnoreCase) ||
                     sheets[0].StartsWith("Sheets", StringComparison.OrdinalIgnoreCase)
            ? sheets[0]
            : "";

        var bodySheets = string.IsNullOrEmpty(header) ? sheets : sheets.Skip(1).ToList();
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(header))
            sb.AppendLine(TrimBlock(header, 800));

        IEnumerable<string> ordered = bodySheets;
        if (!string.IsNullOrWhiteSpace(question))
        {
            var tokens = Tokenize(question);
            ordered = bodySheets
                .Select(s => (s, score: tokens.Count(t => s.Contains(t, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(x => x.score)
                .Select(x => x.s);
        }

        foreach (var sheet in ordered)
        {
            if (sb.Length >= budget) break;
            var room = budget - sb.Length;
            sb.AppendLine(TrimBlock(sheet, Math.Min(3_500, room)));
            sb.AppendLine();
        }

        var packed = TrimBlock(sb.ToString(), budget);
        return new PackResult(packed, raw.Length, packed.Length,
            $"workbook packed · {packed.Length:N0} of {raw.Length:N0}");
    }

    private static PackResult PackGeneric(string raw, int budget, string label)
    {
        var packed = LocalSummaryService.TruncateForAi(raw, budget);
        return new PackResult(packed, raw.Length, packed.Length,
            $"{label} · {packed.Length:N0} of {raw.Length:N0}");
    }

    private static List<string> SplitPages(string raw)
    {
        // PdfPig dump is blank-line separated pages in ContentParserService.
        var parts = Regex.Split(raw.Replace("\r\n", "\n"), @"\n{2,}")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        return parts.Count > 0 ? parts : [raw];
    }

    private static string TrimBlock(string text, int max)
    {
        text = text.Trim();
        if (text.Length <= max) return text;
        return text[..max].TrimEnd() + "\n…(truncated)";
    }

    private static List<string> Tokenize(string q) =>
        Regex.Split(q.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length >= 3)
            .Distinct()
            .Take(12)
            .ToList();
}
