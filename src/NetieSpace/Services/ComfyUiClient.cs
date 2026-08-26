using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NetieSpace.Services;

/// <summary>
/// Local ComfyUI client (default http://127.0.0.1:8188). No WPF types.
/// Probe and queue never throw to the UI -- rembg / Copy stay the offline path.
/// </summary>
public sealed class ComfyUiClient
{
    public const string DefaultBaseUrl = "http://127.0.0.1:8188";
    public const string UrlEnvName = "NETIE_COMFY_URL";
    public const string SidecarSuffix = ".comfy-input.json";
    public const int ProbeTimeoutMs = 1200;
    public const int UploadTimeoutMs = 10_000;
    public const int QueueTimeoutMs = 30_000;
    public const int HistoryPollMs = 400;

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs);
    public static readonly TimeSpan UploadTimeout = TimeSpan.FromMilliseconds(UploadTimeoutMs);
    public static readonly TimeSpan QueueTimeout = TimeSpan.FromMilliseconds(QueueTimeoutMs);

    public string BaseUrl { get; }

    public ComfyUiClient(string? baseUrl = null)
    {
        var raw = baseUrl;
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable(UrlEnvName);
        if (string.IsNullOrWhiteSpace(raw))
            raw = DefaultBaseUrl;
        BaseUrl = raw.Trim().TrimEnd('/');
    }

    public static string OfflineMessage(string? baseUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        return $"ComfyUI is not running at {url}. rembg / Copy still work.";
    }

    /// <summary>
    /// GET /system_stats with ~1.2s timeout. False on any failure. Never throws.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = ProbeTimeout };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(ProbeTimeout);
            using var res = await http.GetAsync($"{BaseUrl}/system_stats", linked.Token).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// POST /upload/image (multipart). Returns Comfy input filename, or null.
    /// Missing file / down / error -> null. Never throws.
    /// </summary>
    public async Task<string?> UploadImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return null;

            using var http = new HttpClient { Timeout = UploadTimeout };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(UploadTimeout);

            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(imagePath, linked.Token).ConfigureAwait(false);
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "image", Path.GetFileName(imagePath));
            form.Add(new StringContent("true"), "overwrite");
            form.Add(new StringContent("input"), "type");

            using var res = await http.PostAsync($"{BaseUrl}/upload/image", form, linked.Token)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return null;

            var text = await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return Path.GetFileName(imagePath);

            using var doc = JsonDocument.Parse(text);
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            var sub = doc.RootElement.TryGetProperty("subfolder", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                return Path.GetFileName(imagePath);
            return string.IsNullOrWhiteSpace(sub) ? name : $"{sub.Trim().TrimEnd('/')}/{name}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// POST /prompt then poll /history/{id} until outputs or ~30s timeout.
    /// Down / error -> Success=false. Never throws to the UI.
    /// </summary>
    public async Task<ComfyUiResult> QueuePromptAsync(string workflowJson, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workflowJson))
                return ComfyUiResult.Fail("No Comfy workflow. rembg / Copy still work.");

            using var http = new HttpClient { Timeout = QueueTimeout };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(QueueTimeout);
            var ct = linked.Token;

            var body = WrapPromptBody(workflowJson);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var post = await http.PostAsync($"{BaseUrl}/prompt", content, ct).ConfigureAwait(false);
            var postText = await post.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!post.IsSuccessStatusCode)
                return ComfyUiResult.Fail(OfflineMessage(BaseUrl)); // Success=false path

            string? promptId = null;
            using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(postText) ? "{}" : postText))
            {
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    return ComfyUiResult.Fail(
                        $"ComfyUI rejected the prompt. rembg / Copy still work.");
                }

                if (doc.RootElement.TryGetProperty("node_errors", out var nodeErrors) &&
                    nodeErrors.ValueKind == JsonValueKind.Object &&
                    nodeErrors.EnumerateObject().Any())
                {
                    return ComfyUiResult.Fail("ComfyUI node errors. rembg / Copy still work.");
                }

                if (doc.RootElement.TryGetProperty("prompt_id", out var pid))
                    promptId = pid.GetString();
            }

            if (string.IsNullOrWhiteSpace(promptId))
                return ComfyUiResult.Fail("ComfyUI returned no prompt id. rembg / Copy still work.");

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < QueueTimeout)
            {
                ct.ThrowIfCancellationRequested();
                using var hist = await http.GetAsync($"{BaseUrl}/history/{promptId}", ct).ConfigureAwait(false);
                var histText = await hist.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (hist.IsSuccessStatusCode &&
                    TryReadHistory(histText, promptId, out var outputs, out var histError))
                {
                    if (histError is not null)
                        return ComfyUiResult.Fail(histError);
                    return ComfyUiResult.Ok($"ComfyUI finished prompt {promptId}.", promptId, outputs);
                }

                await Task.Delay(HistoryPollMs, ct).ConfigureAwait(false);
            }

            return ComfyUiResult.Fail("ComfyUI timed out after 30s. rembg / Copy still work.");
        }
        catch (OperationCanceledException)
        {
            return ComfyUiResult.Fail("ComfyUI timed out. rembg / Copy still work.");
        }
        catch (HttpRequestException)
        {
            return ComfyUiResult.Fail(OfflineMessage(BaseUrl));
        }
        catch (Exception ex)
        {
            return ComfyUiResult.Fail($"ComfyUI failed: {ex.Message}. rembg / Copy still work.");
        }
    }

    /// <summary>
    /// LoadImage + SaveImage graph for POST /prompt (filename only -- Comfy input folder).
    /// SaveImage writes an output Space can GET /view and Copy / drag-out.
    /// </summary>
    public static string BuildLoadImageWorkflow(string imageFileName)
    {
        var name = string.IsNullOrWhiteSpace(imageFileName) ? "image.png" : imageFileName;
        var graph = new Dictionary<string, object>
        {
            ["1"] = new Dictionary<string, object>
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new Dictionary<string, object> { ["image"] = name }
            },
            ["2"] = new Dictionary<string, object>
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["images"] = new object[] { "1", 0 },
                    ["filename_prefix"] = "netie"
                }
            }
        };
        return JsonSerializer.Serialize(graph);
    }

    /// <summary>First images[] entry in a /history outputs object. False if none.</summary>
    public static bool TryFirstOutputImage(
        string? outputsJson,
        out string fileName,
        out string subfolder,
        out string type)
    {
        fileName = "";
        subfolder = "";
        type = "output";
        if (string.IsNullOrWhiteSpace(outputsJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(outputsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var node in doc.RootElement.EnumerateObject())
            {
                if (node.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!node.Value.TryGetProperty("images", out var images) ||
                    images.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var img in images.EnumerateArray())
                {
                    if (img.ValueKind != JsonValueKind.Object)
                        continue;
                    var name = img.TryGetProperty("filename", out var fn) ? fn.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    fileName = name;
                    subfolder = img.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
                    type = img.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output";
                    if (string.IsNullOrWhiteSpace(type))
                        type = "output";
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// GET /view for the first history image and write {stem}.comfy{ext} next to the source.
    /// Missing outputs / down / error -> null. Never throws.
    /// </summary>
    public async Task<string?> DownloadFirstOutputAsync(
        string sourceImagePath,
        string? outputsJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryFirstOutputImage(outputsJson, out var name, out var sub, out var type))
                return null;

            using var http = new HttpClient { Timeout = UploadTimeout };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(UploadTimeout);
            var qs =
                $"filename={Uri.EscapeDataString(name)}" +
                $"&type={Uri.EscapeDataString(type)}" +
                $"&subfolder={Uri.EscapeDataString(sub)}";
            using var res = await http.GetAsync($"{BaseUrl}/view?{qs}", linked.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return null;

            var bytes = await res.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            var ext = Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";
            var dir = Path.GetDirectoryName(sourceImagePath);
            if (string.IsNullOrWhiteSpace(dir))
                dir = ".";
            var stem = Path.GetFileNameWithoutExtension(sourceImagePath);
            if (string.IsNullOrWhiteSpace(stem))
                stem = "comfy";
            var dest = Path.Combine(dir, stem + ".comfy" + ext);
            await File.WriteAllBytesAsync(dest, bytes, linked.Token).ConfigureAwait(false);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Write {file}.comfy-input.json next to the image (sidecar fallback).</summary>
    public static string WriteInputSidecar(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required.", nameof(imagePath));

        var sidecar = imagePath + SidecarSuffix;
        var payload = new Dictionary<string, string>
        {
            ["image_path"] = imagePath,
            ["note"] = "Load this image in local ComfyUI. rembg / Copy still work if Comfy is down."
        };
        System.IO.File.WriteAllText(
            sidecar,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return sidecar;
    }

    private static string WrapPromptBody(string workflowJson)
    {
        using var doc = JsonDocument.Parse(workflowJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("prompt", out _))
        {
            return workflowJson;
        }

        return JsonSerializer.Serialize(new Dictionary<string, JsonElement>
        {
            ["prompt"] = doc.RootElement.Clone()
        });
    }

    private static bool TryReadHistory(
        string json,
        string promptId,
        out string? outputsJson,
        out string? error)
    {
        outputsJson = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(promptId, out var entry))
                return false;

            if (entry.TryGetProperty("status", out var status))
            {
                var statusStr = status.TryGetProperty("status_str", out var ss) ? ss.GetString() : null;
                if (string.Equals(statusStr, "error", StringComparison.OrdinalIgnoreCase))
                {
                    error = "ComfyUI prompt failed. rembg / Copy still work.";
                    return true;
                }
            }

            if (entry.TryGetProperty("outputs", out var outputs) &&
                outputs.ValueKind == JsonValueKind.Object &&
                outputs.EnumerateObject().Any())
            {
                outputsJson = outputs.GetRawText();
                return true;
            }

            if (entry.TryGetProperty("status", out var done) &&
                done.TryGetProperty("completed", out var completed) &&
                completed.ValueKind == JsonValueKind.True)
            {
                outputsJson = "{}";
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}

public sealed record ComfyUiResult(bool Success, string Message, string? PromptId = null, string? OutputJson = null)
{
    public static ComfyUiResult Fail(string message) => new(false, message);
    public static ComfyUiResult Ok(string message, string? promptId = null, string? outputJson = null) =>
        new(true, message, promptId, outputJson);
}
