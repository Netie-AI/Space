using System;
using System.Collections.Generic;
using System.Linq;

namespace NetieSpace.Apps.Image;

/// <summary>A named bundle of option values for one tool: the person picks a purpose instead of typing numbers.</summary>
public sealed record ImagePreset(string Id, string Label, string ToolId, IReadOnlyDictionary<string, string> Options);

public static class ImagePresets
{
    private static IReadOnlyDictionary<string, string> D(params (string K, string V)[] kv) =>
        kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ImagePreset> Table = new[]
    {
        // Resize: the size, and for Instagram the exact square (fill and crop the edges, never stretch).
        new ImagePreset("instagram-square-1080", "Instagram square 1080", "img-resize",
            D(("mode", "pixels"), ("width", "1080"), ("height", "1080"), ("keep_ratio", "true"), ("fit", "cover"))),
        new ImagePreset("web-1600", "Web 1600", "img-resize",
            D(("mode", "pixels"), ("width", "1600"), ("height", ""), ("keep_ratio", "true"), ("fit", "inside"))),
        new ImagePreset("email-small", "Email small", "img-resize",
            D(("mode", "pixels"), ("width", "800"), ("height", ""), ("keep_ratio", "true"), ("fit", "inside"))),

        // Compress: the file size to stay under. The level picks the starting quality, the target steps it down.
        new ImagePreset("instagram-square-1080", "Instagram square 1080", "img-compress",
            D(("level", "low"), ("target_kb", "300"))),
        new ImagePreset("web-1600-300kb", "Web 1600 under 300 KB", "img-compress",
            D(("level", "low"), ("target_kb", "300"))),
        new ImagePreset("email-small", "Email small", "img-compress",
            D(("level", "recommended"), ("target_kb", "150"))),
    };

    public static IReadOnlyList<ImagePreset> Every => Table;

    public static IReadOnlyList<ImagePreset> For(string toolId) =>
        Table.Where(p => string.Equals(p.ToolId, toolId, StringComparison.Ordinal)).ToList();

    /// <summary>True when every value the preset sets is what the options hold now, so the page can mark it picked.</summary>
    public static bool Matches(ImagePreset preset, Func<string, string?> current) =>
        preset.Options.All(kv => string.Equals((current(kv.Key) ?? "").Trim(), kv.Value, StringComparison.OrdinalIgnoreCase));
}
