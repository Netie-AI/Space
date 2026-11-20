namespace NetieSpace.Apps.Shell;

/// <summary>
/// One Segoe Fluent Icons glyph per catalog tool id, so tiles in a category never share a picture.
/// Pure data: codepoints are from Microsoft's Segoe Fluent Icons chart. The view draws them with
/// "Segoe Fluent Icons, Segoe MDL2 Assets" and falls back to the category line drawing when neither font is installed.
/// </summary>
public static class ToolGlyphs
{
    public const string FontFamilyName = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private static readonly Dictionary<string, int> Table = new(StringComparer.Ordinal)
    {
        // Netie PDF - Organize PDF
        ["pdf-merge"] = 0xEA3C, ["pdf-split"] = 0xF740, ["pdf-remove-pages"] = 0xE74D,
        ["pdf-extract-pages"] = 0xEDE1, ["pdf-organize"] = 0xF246, ["pdf-scan"] = 0xE8FE,
        // Optimize PDF
        ["pdf-compress"] = 0xF012, ["pdf-repair"] = 0xE90F, ["pdf-ocr"] = 0xF683,
        // Convert to PDF
        ["pdf-from-jpg"] = 0xE8B9, ["pdf-from-word"] = 0xE8A5, ["pdf-from-powerpoint"] = 0xE786,
        ["pdf-from-excel"] = 0xF0E2, ["pdf-from-html"] = 0xE943,
        // Convert from PDF
        ["pdf-to-jpg"] = 0xEB9F, ["pdf-to-word"] = 0xE8D2, ["pdf-to-powerpoint"] = 0xE7AA,
        ["pdf-to-excel"] = 0xECA5, ["pdf-to-pdfa"] = 0xE8F1,
        // Edit PDF
        ["pdf-rotate"] = 0xE7AD, ["pdf-page-numbers"] = 0xE8FD, ["pdf-watermark"] = 0xE7A1,
        ["pdf-crop"] = 0xE7A8, ["pdf-edit"] = 0xE70F, ["pdf-forms"] = 0xE73A,
        // PDF Security
        ["pdf-unlock"] = 0xE785, ["pdf-protect"] = 0xE72E, ["pdf-sign-self"] = 0xEF3F,
        ["pdf-sign-request"] = 0xE724, ["pdf-redact"] = 0xED1A, ["pdf-compare"] = 0xE8AB,
        // PDF Intelligence
        ["pdf-summarize"] = 0xEB78, ["pdf-translate"] = 0xF1D4, ["pdf-to-markdown"] = 0xE9A4,
        // More
        ["pdf-smart-split"] = 0xE99A, ["pdf-validate-pdfa"] = 0xF760, ["pdf-extract-text"] = 0xEC34,

        // Netie Image - Convert
        ["img-convert-any"] = 0xE895, ["img-dpi"] = 0xEDA8, ["img-from-jpg"] = 0xE91B,
        ["img-to-jpg"] = 0xEB9F, ["img-html-to-image"] = 0xEE71,
        // Create
        ["img-meme"] = 0xE899, ["img-photo-editor"] = 0xEB3C,
        // Modify
        ["img-crop"] = 0xE7A8, ["img-resize"] = 0xE741, ["img-rotate"] = 0xE7AD,
        // Optimize
        ["img-compress"] = 0xF165, ["img-remove-bg"] = 0xEF1F, ["img-repair"] = 0xE90F, ["img-upscale"] = 0xE9E3,
        // Security
        ["img-blur-face"] = 0xEABE, ["img-watermark"] = 0xE7A1,

        // Netie Video - Audio
        ["vid-add-audio"] = 0xEC4F, ["vid-audio-cut-merge"] = 0xE8C6, ["vid-extract-audio"] = 0xE8D6,
        ["vid-mute"] = 0xE74F, ["vid-tts"] = 0xEFA9, ["vid-volume"] = 0xE767,
        // Convert and optimise
        ["vid-compress"] = 0xF165, ["vid-convert"] = 0xE8AB, ["vid-gif-editor"] = 0xF4A9, ["vid-to-gif"] = 0xE7C5,
        // Create
        ["vid-meme"] = 0xE899, ["vid-slideshow"] = 0xE786,
        // Cut and assemble
        ["vid-loop"] = 0xE8EE, ["vid-merge"] = 0xEA3C, ["vid-split"] = 0xF740, ["vid-trim"] = 0xE78A,
        // Effects and colour
        ["vid-adjust"] = 0xE9E9, ["vid-filters"] = 0xE794,
        // Overlays and text
        ["vid-add-image"] = 0xE8B9, ["vid-add-text"] = 0xEC34, ["vid-remove-logo"] = 0xE75C, ["vid-stickers"] = 0xF4AA,
        // Recording
        ["vid-audio-record"] = 0xE720, ["vid-editor"] = 0xE70F, ["vid-presentation-record"] = 0xF71C,
        ["vid-screen-record"] = 0xEE51, ["vid-webcam-record"] = 0xE8B8,
        // Speed and time
        ["vid-reverse"] = 0xE7A7, ["vid-speed"] = 0xEC4A, ["vid-stop-motion"] = 0xE722,
        // Subtitles and speech
        ["vid-auto-subtitles"] = 0xEAD8, ["vid-subtitles"] = 0xED1E, ["vid-translate"] = 0xF1D4,
        // Transform
        ["vid-crop"] = 0xE7A8, ["vid-flip"] = 0xF1CB, ["vid-resize"] = 0xE741, ["vid-rotate"] = 0xE7AD,
        ["vid-stabilize"] = 0xF1A2,
    };

    public static IReadOnlyCollection<string> ToolIds => Table.Keys;

    /// <summary>The glyph character for a tool id, or null when the id has no entry (the view then draws the category line icon).</summary>
    public static string? For(string? toolId) =>
        toolId is not null && Table.TryGetValue(toolId, out var cp) ? char.ConvertFromUtf32(cp) : null;

    /// <summary>The raw codepoint, for tests.</summary>
    public static int? CodePoint(string? toolId) =>
        toolId is not null && Table.TryGetValue(toolId, out var cp) ? cp : null;
}
