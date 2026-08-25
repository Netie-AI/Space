using System.Net.Http;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Syncs Netie file-chats into AirGPT RAG chatspace (backend on :8765).
/// Chat title = file name ("Add to chat" → same named chat). Soft-fail offline.
/// </summary>
public static class AirGptChatSync
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string AirGptBaseUrl =>
        (Environment.GetEnvironmentVariable("AIRGPT_URL")
         ?? Environment.GetEnvironmentVariable("OPENIDE_URL")
         ?? "http://127.0.0.1:8765").TrimEnd('/');

    /// <summary>
    /// Ensure a Netie Space RAG space exists and a chat titled with the file name.
    /// </summary>
    public static async Task EnsureFileChatAsync(string filePath, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        var chatTitle = string.IsNullOrWhiteSpace(title)
            ? DocumentChatStore.TitleForPath(filePath)
            : title.Trim();

        try
        {
            var spaceId = await EnsureNetieSpaceAsync().ConfigureAwait(false);
            if (spaceId is null) return;

            var listUrl = $"{AirGptBaseUrl}/api/rag/spaces/{spaceId}/chats";
            using var listRes = await Http.GetAsync(listUrl).ConfigureAwait(false);
            if (listRes.IsSuccessStatusCode)
            {
                using var listDoc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (listDoc.RootElement.TryGetProperty("chats", out var chats) &&
                    chats.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in chats.EnumerateArray())
                    {
                        var t = c.TryGetProperty("title", out var te) ? te.GetString() : null;
                        if (string.Equals(t, chatTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Info($"airgpt-sync: chat already exists for {chatTitle}");
                            return;
                        }
                    }
                }
            }

            var createUrl = $"{AirGptBaseUrl}/api/rag/spaces/{spaceId}/chats";
            using var body = new StringContent(
                JsonSerializer.Serialize(new { title = chatTitle }, JsonOpts),
                Encoding.UTF8,
                "application/json");
            using var createRes = await Http.PostAsync(createUrl, body).ConfigureAwait(false);
            if (createRes.IsSuccessStatusCode)
                AppLogger.Info($"airgpt-sync: created chat '{chatTitle}' in space {spaceId}");
            else
                AppLogger.Warn($"airgpt-sync: create chat HTTP {(int)createRes.StatusCode}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"airgpt-sync: {ex.Message}");
        }
    }

    private static async Task<int?> EnsureNetieSpaceAsync()
    {
        try
        {
            using var get = await Http.GetAsync($"{AirGptBaseUrl}/api/rag/spaces").ConfigureAwait(false);
            if (get.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (doc.RootElement.TryGetProperty("spaces", out var spaces) &&
                    spaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in spaces.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.Equals(name, "Netie Space", StringComparison.OrdinalIgnoreCase) &&
                            s.TryGetProperty("id", out var idEl) &&
                            idEl.TryGetInt32(out var id))
                            return id;
                    }
                }
            }

            using var body = new StringContent(
                JsonSerializer.Serialize(new { name = "Netie Space" }, JsonOpts),
                Encoding.UTF8,
                "application/json");
            using var post = await Http.PostAsync($"{AirGptBaseUrl}/api/rag/spaces", body).ConfigureAwait(false);
            if (!post.IsSuccessStatusCode) return null;
            using var created = JsonDocument.Parse(await post.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = created.RootElement;
            if (root.TryGetProperty("space", out var space) &&
                space.TryGetProperty("id", out var sid) &&
                sid.TryGetInt32(out var newId))
                return newId;
            if (root.TryGetProperty("id", out var topId) && topId.TryGetInt32(out var tid))
                return tid;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"airgpt-sync space: {ex.Message}");
        }
        return null;
    }
}
