using System.Text;
using System.Text.RegularExpressions;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// Free offline extractive summary — no API. Used before / instead of paid models.
/// Ponytail-inspired ladder: prefer local / skip AI when the document is already small enough.
/// </summary>
public static class LocalSummaryService
{
    /// <summary>Below this size, a solid local extract is usually enough — skip the API.</summary>
    public const int SmallDocChars = 2_500;

    public static string Build(PreviewContent content, int maxBullets = 5)
    {
        var text = content.RawText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            return $"• {content.FileName}\n• Type: {content.Kind}\n• Size: {FormatSize(content.FileSizeBytes)}\n• Open the preview to read the full document.";
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

    /// <summary>
    /// True when calling a remote model would add little value (small text, metadata-only,
    /// or a local extract that already has usable bullets). Images always need the API for vision.
    /// </summary>
    public static bool IsSufficientWithoutAi(PreviewContent content, string localSummary)
    {
        if (content.Kind == PreviewKind.Image)
            return false;

        var rawLen = content.RawText?.Length ?? 0;
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

    public static string SanitizeError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Summary unavailable.";

        // Prefer parsed API message when the payload looks like JSON
        if (raw.Contains('{') || raw.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = AiService.ParseApiErrorMessage(raw);
            if (!string.IsNullOrWhiteSpace(parsed) &&
                !parsed.Contains('{') &&
                parsed.Length < 160)
                return parsed + " Showing local free summary.";
        }

        // Never show raw JSON / huge dumps in the summary strip
        if (raw.Contains("All AI providers failed", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("AI providers unavailable", StringComparison.OrdinalIgnoreCase))
            return "AI summary skipped (quota/size). Showing local free summary instead.";

        if (raw.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Request too large", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("token limit", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("TPM", StringComparison.OrdinalIgnoreCase))
            return "Provider limit reached — switched to a smaller free summary.";

        if (raw.Contains("key not allowed", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("cannot access this model", StringComparison.OrdinalIgnoreCase))
            return "SEA-LION model not allowed for this key — using local free summary.";

        if (raw.Contains("402", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("credits", StringComparison.OrdinalIgnoreCase))
            return "Provider needs credits — using free local / Gemini summary.";

        if (raw.Length > 160 || raw.Contains('{') || raw.Contains("\"error\""))
            return "AI summary unavailable. Showing local free summary.";

        return raw.Length > 180 ? raw[..177] + "…" : raw;
    }

    /// <summary>Short user-facing message for chat/lookup failures (not the summary strip).</summary>
    public static string FriendlyChatFailure(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "AI request failed.";

        if (raw.Contains('{') || raw.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = AiService.ParseApiErrorMessage(raw);
            if (!string.IsNullOrWhiteSpace(parsed) && !parsed.Contains('{'))
                return parsed;
        }

        if (LooksLikeProviderFailure(raw))
            return "AI providers unavailable (quota, size, model access, or credits). Try Switch AI, or add another key.";

        // Never dump long / JSON-looking text into chat
        if (raw.Length > 180 || raw.Contains('{'))
            return "AI request failed. Try Switch AI or check API Keys.";

        return raw;
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
