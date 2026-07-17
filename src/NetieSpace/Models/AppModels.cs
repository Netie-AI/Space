namespace NetieSpace.Models;

public enum PreviewKind
{
    Unknown,
    Text,
    Markdown,
    Image,
    Pdf,
    Html,
    Office,
    Video,
    Binary,
    ShellInfo
}

public sealed class PreviewContent
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public PreviewKind Kind { get; init; }
    public string? RawText { get; set; }
    public string? RenderedHtml { get; init; }
    public string? ImageBase64 { get; init; }
    public string? MimeType { get; init; }
    public string? HtmlFilePath { get; init; }

    public long FileSizeBytes { get; init; }
}

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class AiProviderSettings
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string VisionModel { get; set; } = string.Empty;
    /// <summary>Optional smaller model for huge-doc summaries (TPM-safe).</summary>
    public string SummaryModel { get; set; } = string.Empty;
}

public sealed class AiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    /// <summary>Default chat model (Groq free-tier safe).</summary>
    public string Model { get; set; } = "llama-3.1-8b-instant";
    /// <summary>Vision-capable model (llama-3.2-90b-vision-preview is decommissioned).</summary>
    public string VisionModel { get; set; } = "meta-llama/llama-4-scout-17b-16e-instruct";
    /// <summary>Preferred for summaries / huge docs (lower TPM pressure).</summary>
    public string SummaryModel { get; set; } = "llama-3.1-8b-instant";
    public int MaxTokens { get; set; } = 1024;
    public string SummaryPrompt { get; set; } = string.Empty;
    public List<AiProviderSettings> Fallbacks { get; set; } = [];
}

public sealed class PreviewSettings
{
    public int AnimationDurationMs { get; set; } = 220;
    public bool CacheSummaries { get; set; } = true;
    public int MaxImageDimension { get; set; } = 1024;
}

public sealed class ConversionSettings
{
    public string PythonPath { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
}

public sealed class LicenseSettings
{
    /// <summary>Cloudflare Worker base URL (no trailing slash).</summary>
    public string ApiBaseUrl { get; set; } = "https://netie-space-license.workers.dev";
    /// <summary>Client-side HMAC pepper for device hash (not the server JWT secret).</summary>
    public string DevicePepper { get; set; } = "netie-dev-pepper";
    /// <summary>When true, skip login gate (local development).</summary>
    public bool SkipGate { get; set; } = false;
}

public sealed class AppSettings
{
    public AiSettings Ai { get; set; } = new();
    public PreviewSettings Preview { get; set; } = new();
    public ConversionSettings Conversion { get; set; } = new();
    public LicenseSettings License { get; set; } = new();
}
