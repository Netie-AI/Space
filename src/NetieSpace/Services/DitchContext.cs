using System.Text;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// DeepSeek-style ditch tiers for chat history (SWA / CSA / HCA) — Space-side port of
/// AirGPT ditch_context. Keeps system + last user; digests mid turns; summarizes old.
/// See docs/CORTEX_RUNTIME_BEST_FINAL.md. Optio is not used (K8s swarm — wrong runtime).
/// </summary>
public static class DitchContext
{
    public const int DefaultBudgetChars = 12_000;

    public static List<ChatMessage> Apply(
        IReadOnlyList<ChatMessage> messages,
        int budgetChars = DefaultBudgetChars)
    {
        if (messages.Count == 0)
            return [];

        // Never ditch system or the last user message.
        var system = messages.Where(m => m.Role == "system").ToList();
        var rest = messages.Where(m => m.Role != "system").ToList();
        if (rest.Count == 0)
            return system.ToList();

        var lastUserIdx = rest.FindLastIndex(m => m.Role == "user");
        if (lastUserIdx < 0)
            lastUserIdx = rest.Count - 1;

        // SWA: keep last N turns fully (≈ 6 messages)
        const int swaKeep = 6;
        var swaStart = Math.Max(0, rest.Count - swaKeep);
        // Ensure last user is inside SWA window
        swaStart = Math.Min(swaStart, lastUserIdx);

        var swa = rest.Skip(swaStart).ToList();
        var older = rest.Take(swaStart).ToList();

        var result = new List<ChatMessage>();
        result.AddRange(system);

        // HCA: one paragraph from old turns + pinned notes
        if (older.Count > 0)
        {
            var pins = older.Where(m =>
                    m.Role == "note" ||
                    m.Content.Contains("remember", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(m => Trim(m.Content, 200));
            var digest = SummarizeOld(older);
            var hca = new StringBuilder();
            hca.AppendLine("[Earlier conversation digest]");
            hca.AppendLine(digest);
            foreach (var p in pins)
                hca.AppendLine("• pin: " + p);
            result.Add(new ChatMessage { Role = "system", Content = hca.ToString().Trim() });
        }

        // CSA: mid-window attachments / long dumps → short digests
        foreach (var m in swa)
        {
            if (m.Content.Length > 2_500 && m.Content.Contains("Attached context", StringComparison.Ordinal))
            {
                result.Add(new ChatMessage
                {
                    Role = m.Role,
                    Content = LocalSummaryService.TruncateForAi(m.Content, 1_200)
                });
            }
            else if (m.Content.Length > 4_000)
            {
                result.Add(new ChatMessage
                {
                    Role = m.Role,
                    Content = LocalSummaryService.TruncateForAi(m.Content, 2_000)
                });
            }
            else
            {
                result.Add(m);
            }
        }

        // Hard budget: shrink from the oldest non-system if still over
        while (TotalChars(result) > budgetChars && result.Count > 2)
        {
            var idx = result.FindIndex(m =>
                m.Role != "system" ||
                (m.Content.StartsWith("[Earlier", StringComparison.Ordinal)));
            if (idx < 0 || idx == result.Count - 1)
                break;
            // Prefer shrinking digest / mid messages
            if (result[idx].Content.Length > 400)
            {
                result[idx] = new ChatMessage
                {
                    Role = result[idx].Role,
                    Content = LocalSummaryService.TruncateForAi(result[idx].Content, 400)
                };
            }
            else
            {
                result.RemoveAt(idx);
            }
        }

        return result;
    }

    private static int TotalChars(IEnumerable<ChatMessage> msgs) =>
        msgs.Sum(m => m.Content?.Length ?? 0);

    private static string SummarizeOld(IReadOnlyList<ChatMessage> older)
    {
        var bits = older
            .Where(m => m.Role is "user" or "assistant" or "note")
            .TakeLast(8)
            .Select(m => $"{m.Role}: {Trim(m.Content, 120)}");
        return string.Join(" · ", bits);
    }

    private static string Trim(string s, int n)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
