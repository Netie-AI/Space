using System.Net.Http;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Thin soft-fail client for Cortex engine (:8010). Space materializes pages; Cortex orchestrates.
/// </summary>
public static class CortexClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("CORTEX_API_URL") ?? "http://127.0.0.1:8010").TrimEnd('/');

    public sealed record DocAskResult(bool Ok, string Answer, string? Error, string? RunId);

    public static async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await Http.GetAsync($"{BaseUrl}/health", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// MVP long-doc path: Space-built rag_corpus → POST /api/engine/run architecture_preset=rag.
    /// </summary>
    public static async Task<DocAskResult> AskWithRagAsync(
        string question,
        IReadOnlyList<(string Id, string Text, int? Page)> corpus,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        try
        {
            var rag = corpus.Select(c => new Dictionary<string, object?>
            {
                ["id"] = c.Id,
                ["text"] = c.Text,
                ["meta"] = c.Page is null ? null : new Dictionary<string, object?> { ["page"] = c.Page }
            }).ToList();

            var body = new Dictionary<string, object?>
            {
                ["architecture_preset"] = "rag",
                ["prompt"] = question,
                ["rag_corpus"] = rag,
                ["session_id"] = sessionId ?? Guid.NewGuid().ToString("N")
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/engine/run");
            AttachAuth(req);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new DocAskResult(false, "", $"HTTP {(int)res.StatusCode}: {Trim(json, 200)}", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var answer = ExtractAnswer(root);
            var runId = root.TryGetProperty("run_id", out var rid) ? rid.GetString() : null;
            if (string.IsNullOrWhiteSpace(answer))
                return new DocAskResult(false, "", "Empty Cortex answer", runId);

            return new DocAskResult(true, answer!, null, runId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Cortex AskWithRag: {ex.Message}");
            return new DocAskResult(false, "", ex.Message, null);
        }
    }

    /// <summary>V2: document_qa workflow when template exists; soft-fails otherwise.</summary>
    public static async Task<DocAskResult> AskDocumentQaAsync(
        string question,
        IReadOnlyList<Dictionary<string, object?>> pages,
        CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, object?>
            {
                ["template_id"] = "document_qa",
                ["goal"] = question,
                ["variables"] = new Dictionary<string, object?>
                {
                    ["question"] = question,
                    ["pages"] = pages,
                    ["budget_chars"] = 12_000
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/workflows/run");
            AttachAuth(req);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new DocAskResult(false, "", $"workflow HTTP {(int)res.StatusCode}", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var taskId = root.TryGetProperty("task_id", out var tid) ? tid.GetString()
                : root.TryGetProperty("id", out var id) ? id.GetString() : null;

            // Sync-ish: some deployments return result inline
            var inline = ExtractAnswer(root);
            if (!string.IsNullOrWhiteSpace(inline))
                return new DocAskResult(true, inline!, null, taskId);

            if (string.IsNullOrWhiteSpace(taskId))
                return new DocAskResult(false, "", "No task_id", null);

            // Poll briefly
            for (var i = 0; i < 40; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct).ConfigureAwait(false);
                using var poll = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/workflows/task/{taskId}");
                AttachAuth(poll);
                using var pollRes = await Http.SendAsync(poll, ct).ConfigureAwait(false);
                var pollJson = await pollRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!pollRes.IsSuccessStatusCode) continue;
                using var pollDoc = JsonDocument.Parse(pollJson);
                var status = pollDoc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : "";
                if (status is "completed" or "done" or "succeeded")
                {
                    var ans = ExtractAnswer(pollDoc.RootElement);
                    return string.IsNullOrWhiteSpace(ans)
                        ? new DocAskResult(false, "", "Empty workflow answer", taskId)
                        : new DocAskResult(true, ans!, null, taskId);
                }

                if (status is "failed" or "error")
                    return new DocAskResult(false, "", "Workflow failed", taskId);
            }

            return new DocAskResult(false, "", "Workflow timeout", taskId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Cortex document_qa: {ex.Message}");
            return new DocAskResult(false, "", ex.Message, null);
        }
    }

    private static void AttachAuth(HttpRequestMessage req)
    {
        var key = Environment.GetEnvironmentVariable("CORTEX_API_KEY")
                  ?? Environment.GetEnvironmentVariable("NETIE_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.TryAddWithoutValidation("X-API-Key", key);
    }

    private static string? ExtractAnswer(JsonElement root)
    {
        foreach (var key in new[] { "answer", "result", "output", "text", "message" })
        {
            if (root.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.String)
                    return el.GetString();
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("answer", out var nested))
                    return nested.GetString();
            }
        }

        if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in nodes.EnumerateObject().Reverse())
            {
                if (prop.Value.TryGetProperty("output", out var o))
                {
                    var s = o.ValueKind == JsonValueKind.String ? o.GetString() : o.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }

        return null;
    }

    private static string Trim(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";
}
