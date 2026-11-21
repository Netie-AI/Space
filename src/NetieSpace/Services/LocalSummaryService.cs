using System.Text;
using System.Text.RegularExpressions;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// Free offline extractive summary — no API. Used before / instead of paid models.
/// Ponytail-inspired ladder: prefer local / skip AI when the document is already small enough.
/// </summary>
public static partial class LocalSummaryService
{
    /// <summary>Below this size, a solid local extract is usually enough — skip the API.</summary>
    public const int SmallDocChars = 2_500;

    public static string Build(PreviewContent content, int maxBullets = 5)
    {
        var text = ReadableText(content);
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            var meta = $"• {content.FileName}\n• Type: {content.Kind}\n• Size: {FormatSize(content.FileSizeBytes)}";
            // Only a document has a "full document" to open. A picture or a clip does not.
            return IsDocumentKind(content.Kind)
                ? meta + "\n• Open the preview to read the full document."
                : meta;
        }

        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 20)
            .Where(l => !l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var headings = lines
            .Where(l => l.StartsWith('#') || IsLikelyHeading(l))
            .Select(Clean)
            .Where(l => l.Length is > 8 and < 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxBullets)
            .ToList();

        if (headings.Count >= 2)
            return string.Join("\n", headings.Select(h => $"• {h}"));

        // First substantial sentences
        var blob = string.Join(" ", lines.Take(40));
        var sentences = Regex.Split(blob, @"(?<=[\.!\?])\s+")
            .Select(Clean)
            .Where(s => s.Length is > 40 and < 220)
            .Take(maxBullets)
            .ToList();

        if (sentences.Count == 0)
            return $"• {content.FileName}\n• {Clean(lines.FirstOrDefault() ?? content.Kind.ToString())}";

        return string.Join("\n", sentences.Select(s => $"• {s}"));
    }

    /// <summary>Kinds that have a readable document behind the peek, as opposed to a picture or a clip.</summary>
    private static bool IsDocumentKind(PreviewKind kind) =>
        kind is PreviewKind.Pdf or PreviewKind.Office or PreviewKind.Html or PreviewKind.Markdown or PreviewKind.Text;

    /// <summary>
    /// The text a person would read in this document. RawText is that text for most kinds, but the
    /// Office paths that render through the engine, PowerPoint or LibreOffice put a LABEL there
    /// ("DOCX preview") and keep the words in RenderedHtml. A label is not text: read the page.
    /// </summary>
    public static string ReadableText(PreviewContent content)
    {
        var raw = content.RawText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content.RenderedHtml))
            return raw;
        if (!string.IsNullOrWhiteSpace(raw) && raw.Trim().Length >= 40 && !IsPreviewLabel(raw))
            return raw;

        var fromHtml = HtmlToText(content.RenderedHtml!);
        return fromHtml.Length > raw.Trim().Length ? fromHtml : raw;
    }

    /// <summary>"DOCX preview", "PPTX preview", "XLSX preview": a tag for the kind, not content.</summary>
    private static bool IsPreviewLabel(string raw) =>
        Regex.IsMatch(raw.Trim(), @"^[A-Za-z]{3,5} preview$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Visible text of an HTML page, one line per block (paragraph, heading, list item, table row).
    /// Scripts, styles and the head are dropped, entities are decoded.
    /// </summary>
    public static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var s = Regex.Replace(html, @"<!--.*?-->", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"<(script|style|head|svg|noscript)\b.*?</\1\s*>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</(p|div|li|ul|ol|h[1-6]|tr|table|section|article|blockquote|pre|details|summary|figure|figcaption)\s*>",
            "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</t[dh]\s*>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);

        var lines = s.Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => Regex.Replace(l, @"[ \t\u00A0]+", " ").Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// True when calling a remote model would add little value (small text, metadata-only,
    /// or a local extract that already has usable bullets). Images always need the API for vision.
    /// </summary>
    public static bool IsSufficientWithoutAi(PreviewContent content, string localSummary)
    {
        if (content.Kind == PreviewKind.Image)
            return false;

        var rawLen = ReadableText(content).Length;
        if (rawLen == 0)
            return true;

        if (rawLen > SmallDocChars)
            return false;

        if (rawLen < 40)
            return true;

        var bullets = localSummary
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(l => l.StartsWith('•') || l.StartsWith('-'));

        // Small file with a real extractive outline → skip tokens.
        return bullets >= 2;
    }

    /// <summary>Compact document excerpt safe for free-tier TPM limits.</summary>
    public static string TruncateForAi(string? text, int maxChars = 7000)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(no extractable text)";

        text = text.Trim();
        if (text.Length <= maxChars)
            return text;

        var head = text[..(maxChars * 2 / 3)];
        var tail = text[^(maxChars / 3)..];
        return head + "\n\n[…middle omitted for free-tier limits…]\n\n" + tail;
    }

    public static bool LooksLikeProviderFailure(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (raw.StartsWith("All AI providers failed", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("AI providers unavailable", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("key_model_access_denied", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("key not allowed", StringComparison.OrdinalIgnoreCase))
            return true;

        return raw.Contains('{') && raw.Contains("error", StringComparison.OrdinalIgnoreCase) && raw.Length > 80;
    }

    private static bool IsLikelyHeading(string line) =>
        line.Length < 80 &&
        !line.EndsWith('.') &&
        (char.IsUpper(line[0]) || line.All(c => !char.IsLetter(c) || char.IsUpper(c)));

    private static string Clean(string s) =>
        Regex.Replace(s, @"\s+", " ").Trim().TrimStart('#', '-', '*', ' ');

    private static string FormatSize(long bytes)
    {
        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }
}
