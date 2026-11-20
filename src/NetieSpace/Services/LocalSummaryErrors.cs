namespace NetieSpace.Services;

/// <summary>
/// User-facing error text for the summary strip and chat. Split from LocalSummaryService.cs
/// because it reads AiService; the extractive half of LocalSummaryService has no such
/// dependency and is compiled into the Linux test project.
/// </summary>
public static partial class LocalSummaryService
{
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
}
