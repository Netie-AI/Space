using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Services;

public sealed class AiService
{
    private const int ChatContextChars = 9000;
    private const int ChatContextRepeatChars = 1_800;
    private const int SummaryCharsNormal = 6500;
    private const int SummaryCharsHuge = 3500;
    private const int HugeDocChars = 12_000;

    private readonly AiSettings _settings;
    private readonly SummaryCache _cache;
    private readonly ConversationMemoryCache _memory;
    private readonly List<ProviderClient> _providers;
    private int _preferredIndex;

    public AiService(AiSettings settings, SummaryCache cache, ConversationMemoryCache? memory = null)
    {
        _settings = settings;
        _cache = cache;
        _memory = memory ?? new ConversationMemoryCache();
        _providers = BuildProviders(settings);
    }

    public bool IsConfigured => _providers.Count > 0;
    public string ActiveProvider =>
        _providers.Count == 0 ? "none" : _providers[Math.Clamp(_preferredIndex, 0, _providers.Count - 1)].Name;

    public IReadOnlyList<string> ProviderNames => _providers.Select(p => p.Name).ToList();

    public void ReloadProviders()
    {
        foreach (var provider in _providers)
            provider.Http.Dispose();

        _providers.Clear();
        _providers.AddRange(BuildProviders(_settings));
        _preferredIndex = 0;
    }

    public string SwitchToNextProvider()
    {
        if (_providers.Count == 0)
            return "none";

        _preferredIndex = (_preferredIndex + 1) % _providers.Count;
        AppLogger.Info($"AI provider switched to {ActiveProvider}");
        return ActiveProvider;
    }

    public bool SetPreferredProvider(string name)
    {
        var idx = _providers.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return false;
        _preferredIndex = idx;
        AppLogger.Info($"AI provider set to {ActiveProvider}");
        return true;
    }

    public async Task<string> SummarizeAsync(PreviewContent content, CancellationToken ct = default)
    {
        var local = LocalSummaryService.Build(content);

        if (!IsConfigured)
            return local;

        // Rung 1: reuse cached AI summary for the same content fingerprint
        if (_cache.TryGet(content, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            AppLogger.Info("AI summary cache hit — skipped provider call");
            return cached!;
        }

        // Rung 2: small / metadata docs — local extractive summary is enough (0 tokens)
        if (LocalSummaryService.IsSufficientWithoutAi(content, local))
        {
            AppLogger.Info("AI summary skipped — local summary sufficient for small file");
            _cache.Set(content, local);
            return local;
        }

        // Dual-brain: local embedding digest (consent-gated, never leaves device)
        string? brainDigest = null;
        try
        {
            brainDigest = await LocalEmbeddingBrain.Instance.PrepareBeforeSummaryAsync(content, ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Local brain prepare: {ex.Message}");
        }

        var rawLen = content.RawText?.Length ?? 0;
        var huge = rawLen > HugeDocChars;
        // Free-tier friendly: truncate hard + always prefer smaller summary model
        var excerpt = LocalSummaryService.TruncateForAi(
            content.RawText,
            huge ? SummaryCharsHuge : SummaryCharsNormal);

        var userPayload = string.IsNullOrWhiteSpace(brainDigest)
            ? $"File: {content.FileName}\nType: {content.Kind}\n\nExcerpt:\n{excerpt}"
            : $"File: {content.FileName}\nType: {content.Kind}\n\nLocal dual-brain memory:\n{brainDigest}\n\nExcerpt:\n{excerpt}";

        var summary = await CompleteAsync(
        [
            new ChatMessage
            {
                Role = "system",
                Content = "Write 3-5 short bullet points summarizing the document. No JSON. No preamble. Prefer local dual-brain signals when present."
            },
            new ChatMessage
            {
                Role = "user",
                Content = userPayload
            }
        ], useVision: content.Kind == PreviewKind.Image, content, ct,
            maxTokensOverride: 400,
            preferSmallModel: true);

        if (string.IsNullOrWhiteSpace(summary) ||
            summary.StartsWith("All AI providers failed", StringComparison.Ordinal) ||
            LocalSummaryService.LooksLikeProviderFailure(summary))
        {
            AppLogger.Warn($"AI summary failed — using local free summary. Detail: {LocalSummaryService.SanitizeError(summary)}");
            return local;
        }

        _cache.Set(content, summary);
        return summary;
    }

    public async Task<string> ChatAsync(
        PreviewContent content,
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        CancellationToken ct = default)
    {
        var fp = SummaryCache.ContentFingerprint(content);

        // Identical question on the same excerpt → zero tokens (no history dependency)
        if (history.Count == 0 &&
            _memory.TryGetChat(fp, userMessage, out var cachedChat) &&
            !string.IsNullOrWhiteSpace(cachedChat))
        {
            AppLogger.Info("AI chat memory hit — skipped provider call");
            return cachedChat!;
        }

        // Submit() already appended the user turn into history — do not duplicate it.
        var historyForApi = history;
        if (history.Count > 0 &&
            string.Equals(history[^1].Role, "user", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(history[^1].Content, userMessage, StringComparison.Ordinal))
        {
            historyForApi = history.Take(history.Count - 1).ToList();
        }

        // Soft-fail Cortex escalation for long PDF / cross-page asks
        if (PdfPageMaterializer.ShouldEscalateToCortex(content, userMessage) &&
            !string.IsNullOrWhiteSpace(content.FilePath) &&
            content.Kind == PreviewKind.Pdf &&
            await CortexClient.IsHealthyAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var pages = PdfPageMaterializer.ExtractPages(content.FilePath!);
                var corpus = PdfPageMaterializer.ToRagCorpus(pages, userMessage);
                if (corpus.Count > 0)
                {
                    var cortex = await CortexClient.AskWithRagAsync(userMessage, corpus, ct: ct)
                        .ConfigureAwait(false);
                    if (cortex.Ok && !string.IsNullOrWhiteSpace(cortex.Answer) &&
                        !LocalSummaryService.LooksLikeProviderFailure(cortex.Answer))
                    {
                        AppLogger.Info($"Cortex RAG hit · run={cortex.RunId}");
                        _memory.MarkDocumentPrimed(fp);
                        return cortex.Answer;
                    }

                    // V2 soft try: document_qa workflow when template is registered
                    var pagePayload = corpus.Select(c => new Dictionary<string, object?>
                    {
                        ["page"] = c.Page,
                        ["text"] = c.Text,
                        ["page_text"] = c.Text
                    }).ToList();
                    var qa = await CortexClient.AskDocumentQaAsync(userMessage, pagePayload, ct)
                        .ConfigureAwait(false);
                    if (qa.Ok && !string.IsNullOrWhiteSpace(qa.Answer) &&
                        !LocalSummaryService.LooksLikeProviderFailure(qa.Answer))
                    {
                        AppLogger.Info($"Cortex document_qa hit · run={qa.RunId}");
                        _memory.MarkDocumentPrimed(fp);
                        return qa.Answer;
                    }

                    AppLogger.Warn($"Cortex soft-fail: {cortex.Error ?? qa.Error}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Cortex escalation: {ex.Message}");
            }
        }

        var slim = historyForApi.Count > 0;
        var hasAttachmentPayload = userMessage.Contains("Attached context (", StringComparison.Ordinal);
        var modeAddon = ChatModeSystemAddon(userMessage);
        var persona = string.IsNullOrEmpty(modeAddon)
            ? "You are a helpful assistant answering questions about the currently previewed file. " +
              "Be concise and accurate. Prefer short markdown (bold for key terms). " +
              "Use a mermaid fenced block only when a diagram clearly helps.\n\n"
            : modeAddon + "You are answering about the currently previewed file.\n\n";
        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = persona + BuildDocumentPrompt(content, slim: slim || hasAttachmentPayload)
            }
        };

        messages.AddRange(historyForApi);
        messages.Add(new ChatMessage { Role = "user", Content = userMessage });
        messages = DitchContext.Apply(messages);

        var huge = (content.RawText?.Length ?? 0) > HugeDocChars;
        var visionContent = TryHydrateVisionFromMessage(content, userMessage);
        var useVision = visionContent is not null && !string.IsNullOrWhiteSpace(visionContent.ImageBase64);
        // Prefer small model when doc is huge or follow-up (slim remnant) turn
        var reply = await CompleteAsync(
            messages,
            useVision,
            visionContent ?? content,
            ct,
            preferSmallModel: huge || slim);

        if (!string.IsNullOrWhiteSpace(reply) &&
            !LocalSummaryService.LooksLikeProviderFailure(reply))
        {
            _memory.MarkDocumentPrimed(fp);
            if (history.Count == 0)
                _memory.SetChat(fp, userMessage, reply);
        }

        return reply;
    }

    public async Task<string> LookupWordAsync(
        string word,
        string? surroundingContext = null,
        CancellationToken ct = default)
    {
        if (_memory.TryGetLookup(word, surroundingContext, out var cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            AppLogger.Info("AI lookup memory hit — skipped provider call");
            return cached!;
        }

        var prompt = string.IsNullOrWhiteSpace(surroundingContext)
            ? $"Define and explain the word or phrase \"{word}\" clearly and concisely."
            : $"Look up \"{word}\" using this document context. Prefer the in-document sense " +
              $"(location, nearby tokens, summary). If the selection looks truncated or misspelled, " +
              $"correct it from the neighbors and briefly note the correction.\n\n{surroundingContext}";

        var answer = await CompleteAsync(
        [
            new ChatMessage
            {
                Role = "system",
                Content = "You are a concise document-aware reference assistant. Give a short definition " +
                          "tied to the provided context when present, part of speech if applicable, " +
                          "and one usage example that fits the document."
            },
            new ChatMessage { Role = "user", Content = prompt }
        ], useVision: false, null, ct, maxTokensOverride: 320, preferSmallModel: true);

        if (!string.IsNullOrWhiteSpace(answer) &&
            !LocalSummaryService.LooksLikeProviderFailure(answer))
            _memory.SetLookup(word, surroundingContext, answer);

        return answer;
    }

    private async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        bool useVision,
        PreviewContent? content,
        CancellationToken ct,
        int? maxTokensOverride = null,
        bool preferSmallModel = false)
    {
        if (!IsConfigured)
            return "AI is not configured. Open tray → API Keys… to add a free Google / Groq / SEA-LION key.";

        var errors = new StringBuilder();
        var order = BuildAttemptOrder();
        var maxTokens = maxTokensOverride ?? Math.Min(_settings.MaxTokens, 1024);

        foreach (var provider in order)
        {
            try
            {
                var model = ResolveModel(provider, useVision, content, preferSmallModel);
                var payload = BuildRequestPayload(provider, model, messages, useVision, content, maxTokens);
                using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload, JsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
                ApplyProviderRequestHeaders(provider, request);

                using var response = await provider.Http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    var friendly = FormatApiError(provider.Name, (int)response.StatusCode, body);
                    errors.AppendLine(friendly);
                    AppLogger.Warn($"AI {provider.Name} HTTP {(int)response.StatusCode}: {TrimForLog(body)}");

                    // Groq 413 TPM: one retry with smaller model + tighter messages
                    if ((int)response.StatusCode == 413 &&
                        !preferSmallModel &&
                        !string.IsNullOrWhiteSpace(provider.SummaryModel) &&
                        !string.Equals(model, provider.SummaryModel, StringComparison.Ordinal))
                    {
                        var retry = await TryGroqSizeRetryAsync(provider, messages, useVision, content, maxTokens, ct);
                        if (retry is not null)
                        {
                            SetPreferredProvider(provider.Name);
                            return retry;
                        }
                    }

                    if (IsQuotaOrRateLimit((int)response.StatusCode, body))
                    {
                        AppLogger.Warn($"AI quota/rate limit on {provider.Name} — trying next provider");
                        PromoteNextAfter(provider.Name);
                    }

                    continue;
                }

                var result = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
                var text = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SetPreferredProvider(provider.Name);
                    return text;
                }

                errors.AppendLine($"[{provider.Name}] Empty response.");
            }
            catch (Exception ex)
            {
                errors.AppendLine($"[{provider.Name}] {ex.Message}");
                AppLogger.Warn($"AI {provider.Name} exception: {ex.Message}");
            }
        }

        AppLogger.Warn($"All AI providers failed:\n{errors}");
        return LocalSummaryService.FriendlyChatFailure("All AI providers failed.");
    }

    private async Task<string?> TryGroqSizeRetryAsync(
        ProviderClient provider,
        IReadOnlyList<ChatMessage> messages,
        bool useVision,
        PreviewContent? content,
        int maxTokens,
        CancellationToken ct)
    {
        if (!provider.Name.Equals("groq", StringComparison.OrdinalIgnoreCase))
            return null;

        var shrunk = ShrinkMessagesForTpm(messages, SummaryCharsHuge);
        var model = provider.SummaryModel!;
        var payload = BuildRequestPayload(provider, model, shrunk, useVision: false, content: null, maxTokens);
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        ApplyProviderRequestHeaders(provider, request);

        using var response = await provider.Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Warn($"AI groq 413-retry failed: {TrimForLog(body)}");
            return null;
        }

        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
        var text = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string ResolveModel(
        ProviderClient provider,
        bool useVision,
        PreviewContent? content,
        bool preferSmallModel)
    {
        if (useVision && content?.Kind == PreviewKind.Image)
            return provider.VisionModel;

        if (preferSmallModel && !string.IsNullOrWhiteSpace(provider.SummaryModel))
            return provider.SummaryModel!;

        return provider.Model;
    }

    private static object BuildRequestPayload(
        ProviderClient provider,
        string model,
        IReadOnlyList<ChatMessage> messages,
        bool useVision,
        PreviewContent? content,
        int maxTokens)
    {
        // OpenAI-compatible shape for Gemini / Groq / SEA-LION / OpenRouter.
        // SEA-LION instruct models do not need chat_template_kwargs (that's for v3.5 reasoning).
        var apiMessages = messages.Select(m => ToApiMessage(m, useVision && m.Role == "user", content)).ToList();

        if (provider.Name.Equals("sea-lion", StringComparison.OrdinalIgnoreCase) &&
            model.Contains("-R", StringComparison.OrdinalIgnoreCase))
        {
            return new SeaLionCompletionRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Messages = apiMessages,
                ChatTemplateKwargs = new Dictionary<string, string> { ["thinking_mode"] = "off" }
            };
        }

        return new ChatCompletionRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = apiMessages
        };
    }

    private static void ApplyProviderRequestHeaders(ProviderClient provider, HttpRequestMessage request)
    {
        // SEA-LION docs use accept: text/plain on chat/completions
        if (provider.Name.Equals("sea-lion", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation("accept", "text/plain");
    }

    private List<ProviderClient> BuildAttemptOrder()
    {
        if (_providers.Count == 0)
            return [];

        var list = new List<ProviderClient>();
        for (var i = 0; i < _providers.Count; i++)
            list.Add(_providers[(_preferredIndex + i) % _providers.Count]);
        return list;
    }

    private void PromoteNextAfter(string failedName)
    {
        if (_providers.Count < 2)
            return;
        var idx = _providers.FindIndex(p => p.Name.Equals(failedName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return;
        _preferredIndex = (idx + 1) % _providers.Count;
    }

    private static bool IsQuotaOrRateLimit(int status, string body)
    {
        // 413 = request too large / TPM — treat as soft fail and try next (usually smaller) provider
        if (status is 429 or 402 or 403 or 413)
            return true;

        var lower = body.ToLowerInvariant();
        return lower.Contains("rate limit") ||
               lower.Contains("quota") ||
               lower.Contains("insufficient_quota") ||
               lower.Contains("tokens per day") ||
               lower.Contains("request too large") ||
               lower.Contains("tpm") ||
               lower.Contains("key_model_access_denied") ||
               lower.Contains("key not allowed") ||
               lower.Contains("exceeded") ||
               lower.Contains("billing");
    }

    private static List<ProviderClient> BuildProviders(AiSettings settings)
    {
        var list = new List<ProviderClient>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        void Add(
            string name,
            string? key,
            string baseUrl,
            string model,
            string visionModel,
            string? summaryModel = null)
        {
            if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
                return;

            var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(90)
            };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            // Per-provider default headers
            if (name.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "https://netie.space");
                http.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "Netie Space");
            }
            else if (name.Equals("sea-lion", StringComparison.OrdinalIgnoreCase))
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
            }

            list.Add(new ProviderClient(
                name,
                http,
                model,
                visionModel,
                string.IsNullOrWhiteSpace(summaryModel) ? null : summaryModel));
        }

        var groqKey = settings.ApiKey;
        var openRouterEnv = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var seaLionEnv = Environment.GetEnvironmentVariable("SEA_LION_API_KEY");
        // settings.ApiKey may be filled from non-Groq env fallbacks — only treat as Groq when it isn't those keys
        if (!string.IsNullOrWhiteSpace(groqKey) &&
            groqKey != openRouterEnv &&
            groqKey != seaLionEnv)
        {
            Add("groq", groqKey, settings.BaseUrl,
                string.IsNullOrWhiteSpace(settings.Model) ? "llama-3.1-8b-instant" : settings.Model,
                string.IsNullOrWhiteSpace(settings.VisionModel)
                    ? "meta-llama/llama-4-scout-17b-16e-instruct"
                    : settings.VisionModel,
                string.IsNullOrWhiteSpace(settings.SummaryModel)
                    ? "llama-3.1-8b-instant"
                    : settings.SummaryModel);
        }

        // Free first: Google AI Studio (Gemini OpenAI-compatible endpoint)
        var geminiKey =
            Environment.GetEnvironmentVariable("GOOGLE_AISTUDIO_FREE") ??
            Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ??
            Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Add("gemini", geminiKey,
            "https://generativelanguage.googleapis.com/v1beta/openai/",
            "gemini-flash-latest",
            "gemini-flash-latest",
            "gemini-flash-latest");

        // Groq: smaller free-tier-safe defaults; Scout for vision (90b-vision decommissioned)
        Add("groq", Environment.GetEnvironmentVariable("GROQ_API_KEY"),
            "https://api.groq.com/openai/v1",
            "llama-3.3-70b-versatile",
            "meta-llama/llama-4-scout-17b-16e-instruct",
            "llama-3.1-8b-instant");

        // SEA-LION standard-tier: aisingapore/Qwen-SEA-LION-v4.5-27B-IT
        // (NOT sea-lion/lion-gemini-1.5-flash — causes key_model_access_denied)
        Add("sea-lion", Environment.GetEnvironmentVariable("SEA_LION_API_KEY"),
            "https://api.sea-lion.ai/v1",
            "aisingapore/Qwen-SEA-LION-v4.5-27B-IT",
            "aisingapore/Qwen-SEA-LION-v4.5-27B-IT",
            "aisingapore/Gemma-SEA-LION-v4-27B-IT");

        Add("openrouter", Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
            "https://openrouter.ai/api/v1",
            "openai/gpt-4o-mini",
            "openai/gpt-4o-mini",
            "openai/gpt-4o-mini");

        foreach (var fb in settings.Fallbacks)
        {
            Add(fb.Name, fb.ApiKey, fb.BaseUrl, fb.Model, fb.VisionModel,
                string.IsNullOrWhiteSpace(fb.SummaryModel) ? null : fb.SummaryModel);
        }

        return list;
    }

    /// <summary>
    /// If the chat payload staged an image path (`[image] C:\...`), load bytes for vision models.
    /// </summary>
    private static PreviewContent? TryHydrateVisionFromMessage(PreviewContent content, string userMessage)
    {
        if (content.Kind == PreviewKind.Image && !string.IsNullOrWhiteSpace(content.ImageBase64))
            return content;

        var idx = userMessage.IndexOf("[image]", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Kind == PreviewKind.Image ? content : null;

        var line = userMessage[idx..].Split('\n', 2)[0];
        var path = line.Replace("[image]", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return content.Kind == PreviewKind.Image ? content : null;

        try
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length > 4_000_000)
            {
                var trimmed = new byte[4_000_000];
                Buffer.BlockCopy(bytes, 0, trimmed, 0, trimmed.Length);
                bytes = trimmed;
            }
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return new PreviewContent
            {
                FilePath = content.FilePath ?? path,
                FileName = content.FileName ?? System.IO.Path.GetFileName(path),
                Kind = PreviewKind.Image,
                FileSizeBytes = content.FileSizeBytes,
                RawText = content.RawText,
                ImageBase64 = Convert.ToBase64String(bytes),
                MimeType = mime
            };
        }
        catch
        {
            return content.Kind == PreviewKind.Image ? content : null;
        }
    }

    private static ApiMessage ToApiMessage(ChatMessage message, bool attachVision, PreviewContent? content)
    {
        if (!attachVision || content?.ImageBase64 is null)
            return new ApiMessage { Role = message.Role, Content = message.Content };

        return new ApiMessage
        {
            Role = message.Role,
            Content = new List<ContentPart>
            {
                new() { Type = "text", Text = message.Content },
                new()
                {
                    Type = "image_url",
                    ImageUrl = new ImageUrl
                    {
                        Url = $"data:{content.MimeType ?? "image/jpeg"};base64,{content.ImageBase64}"
                    }
                }
            }
        };
    }

    /// <summary>
    /// ChatPanel prefixes the user turn with [Cite mode] / [First Principles mode] / [Mindmap mode].
    /// Overlay the system prompt so Cite/Mindmap are not fighting "be concise + mermaid only when helpful".
    /// </summary>
    internal static string ChatModeSystemAddon(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return string.Empty;

        if (userMessage.StartsWith("[Cite mode]", StringComparison.Ordinal))
        {
            return "Active mode: Cite. Quote the open document. Every non-trivial claim needs a short " +
                   "citation (page number, heading, or a brief excerpt). Do not invent sources.\n\n";
        }

        if (userMessage.StartsWith("[First Principles mode]", StringComparison.Ordinal))
        {
            return "Active mode: First Principles. Strip the question and document to axioms, then rebuild " +
                   "the answer from those axioms. No fluff, no throat-clearing, no analogies unless they are the axiom.\n\n";
        }

        if (userMessage.StartsWith("[Mindmap mode]", StringComparison.Ordinal))
        {
            return "Active mode: Mindmap. Return a compact indented outline of the open document as a mermaid " +
                   "mindmap or nested bullet tree. No prose preamble. Prefer a mermaid fenced block.\n\n";
        }

        return string.Empty;
    }

    private static string BuildDocumentPrompt(PreviewContent content, bool slim = false)
    {
        var fp = SummaryCache.ContentFingerprint(content);
        var rawLen = content.RawText?.Length ?? 0;
        var maxChars = slim
            ? ChatContextRepeatChars
            : rawLen > HugeDocChars
                ? SummaryCharsHuge
                : ChatContextChars;

        var sb = new StringBuilder();
        sb.AppendLine($"File: {content.FileName}");
        sb.AppendLine($"Path: {content.FilePath}");
        sb.AppendLine($"Type: {content.Kind}");
        sb.AppendLine($"Fingerprint: {fp[..Math.Min(16, fp.Length)]}");
        sb.AppendLine();

        if (slim)
        {
            // Repeat Look Up / chat on same excerpt — do not re-send the full document.
            sb.AppendLine("Prior turn already included a longer excerpt for this fingerprint.");
            sb.AppendLine("Use chat history plus this outline and short remnant:");
            sb.AppendLine();
            sb.AppendLine("Outline:");
            sb.AppendLine(LocalSummaryService.Build(content));
            sb.AppendLine();
            sb.AppendLine("Remnant:");
            sb.Append(LocalSummaryService.TruncateForAi(content.RawText, maxChars));
        }
        else
        {
            sb.AppendLine("Content:");
            sb.Append(LocalSummaryService.TruncateForAi(content.RawText, maxChars));
        }

        return sb.ToString();
    }

    private static List<ChatMessage> ShrinkMessagesForTpm(IReadOnlyList<ChatMessage> messages, int maxChars)
    {
        var list = new List<ChatMessage>(messages.Count);
        foreach (var m in messages)
        {
            if (m.Role == "system" && m.Content.Length > maxChars)
            {
                list.Add(new ChatMessage
                {
                    Role = m.Role,
                    Content = LocalSummaryService.TruncateForAi(m.Content, maxChars)
                });
            }
            else
            {
                list.Add(m);
            }
        }

        return list;
    }

    /// <summary>Short human-readable error for logs / aggregated failure — never raw JSON blobs.</summary>
    public static string FormatApiError(string provider, int status, string body)
    {
        var detail = ParseApiErrorMessage(body);
        return string.IsNullOrWhiteSpace(detail)
            ? $"[{provider}] HTTP {status}"
            : $"[{provider}] {detail}";
    }

    public static string ParseApiErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Request failed.";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? message = null;

            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object)
                {
                    if (err.TryGetProperty("message", out var msg))
                        message = msg.GetString();
                    else if (err.TryGetProperty("code", out var code))
                        message = code.GetString();
                }
                else if (err.ValueKind == JsonValueKind.String)
                {
                    message = err.GetString();
                }
            }
            else if (root.TryGetProperty("message", out var topMsg))
            {
                message = topMsg.GetString();
            }

            if (!string.IsNullOrWhiteSpace(message))
                return HumanizeApiMessage(message);
        }
        catch
        {
            // not JSON — fall through
        }

        return HumanizeApiMessage(body.Length > 200 ? body[..200] : body);
    }

    private static string HumanizeApiMessage(string raw)
    {
        var lower = raw.ToLowerInvariant();

        if (lower.Contains("key not allowed") || lower.Contains("key_model_access_denied"))
            return "API key cannot access this model — use a standard-tier model.";
        if (lower.Contains("request too large") || lower.Contains("tpm") || lower.Contains("tokens per minute"))
            return "Request too large for provider token limit.";
        if (lower.Contains("rate limit") || lower.Contains("too many requests") || lower.Contains("quota"))
            return "Rate limit or quota reached.";
        if (lower.Contains("insufficient_quota") || lower.Contains("credits") || lower.Contains("payment"))
            return "Provider needs credits or billing.";
        if (lower.Contains("invalid api key") || lower.Contains("incorrect api key") || lower.Contains("authentication"))
            return "Invalid API key.";
        if (lower.Contains("model") && (lower.Contains("not found") || lower.Contains("decommissioned") || lower.Contains("does not exist")))
            return "Model unavailable — check default model id.";

        // Strip braces / quotes so UI never looks like raw JSON
        var clean = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (clean.Contains('{') || clean.Contains('"'))
        {
            var idx = clean.IndexOf("message", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                clean = clean[idx..];
            clean = clean.Replace("\"", "").Replace("{", "").Replace("}", "");
        }

        return clean.Length > 140 ? clean[..137] + "…" : clean;
    }

    private static string TrimForLog(string body) =>
        body.Length > 400 ? body[..400] + "..." : body;

    private sealed record ProviderClient(
        string Name,
        HttpClient Http,
        string Model,
        string VisionModel,
        string? SummaryModel);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = string.Empty;
        public List<ApiMessage> Messages { get; set; } = [];
        public int MaxTokens { get; set; }
    }

    private sealed class SeaLionCompletionRequest
    {
        public string Model { get; set; } = string.Empty;
        public List<ApiMessage> Messages { get; set; } = [];
        public int MaxTokens { get; set; }

        [JsonPropertyName("chat_template_kwargs")]
        public Dictionary<string, string>? ChatTemplateKwargs { get; set; }
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        public string? Content { get; set; }
    }

    private sealed class ApiMessage
    {
        public string Role { get; set; } = string.Empty;
        public object Content { get; set; } = string.Empty;
    }

    private sealed class ContentPart
    {
        public string Type { get; set; } = string.Empty;
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        public ImageUrl? ImageUrl { get; set; }
    }

    private sealed class ImageUrl
    {
        public string Url { get; set; } = string.Empty;
    }
}
