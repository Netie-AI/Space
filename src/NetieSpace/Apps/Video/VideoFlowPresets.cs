using System;
using System.Collections.Generic;
using System.Linq;

namespace NetieSpace.Apps.Video;

/// <summary>One export target of the Netie Video quick edit (W6-4): what it is called, what it makes.</summary>
internal sealed record FlowPreset(string Id, string Label, string Detail, bool IsGif);

/// <summary>
/// The export presets of the filmstrip editor, and the size estimate shown before a run. The estimate is arithmetic
/// on the selection, not a trial encode: WhatsApp is exact by construction (a two-pass encode aimed at a byte budget,
/// then checked), the others are typical bitrates and say "about" in the UI.
/// </summary>
internal static class VideoFlowPresets
{
    public const string Original = "original";
    public const string WhatsApp = "whatsapp";
    public const string Reel = "reel";
    public const string YouTube = "youtube";
    public const string Gif = "gif";

    /// <summary>What a message can carry. The check script and the engine both hold the output below this.</summary>
    public const long WhatsAppLimitBytes = 16_000_000;

    /// <summary>The byte budget the two-pass encode aims at: leaves room for container overhead and rate-control drift.</summary>
    public const long WhatsAppTargetBytes = 13_500_000;

    public const int WhatsAppAudioBps = 96_000;
    public const int GifFps = 12;
    public const int GifWidth = 480;
    public const int ReelWidth = 1080;
    public const int ReelHeight = 1920;

    public static readonly IReadOnlyList<FlowPreset> All = new[]
    {
        new FlowPreset(Original, "Original quality", "Same picture, only your edits", false),
        new FlowPreset(WhatsApp, "WhatsApp (under 16 MB)", "Sized to fit a message", false),
        new FlowPreset(Reel, "Instagram Reel 1080x1920", "Tall, blurred sides", false),
        new FlowPreset(YouTube, "YouTube 1080p", "Up to 1920x1080, sharp", false),
        new FlowPreset(Gif, "GIF 480 px 12 fps", "Silent, loops", true),
    };

    public static IReadOnlyList<string> Ids => All.Select(p => p.Id).ToList();

    public static FlowPreset Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("unknown export preset '" + id + "'");

    /// <summary>
    /// Video bitrate and frame height of the WhatsApp encode for a clip of <paramref name="outSeconds"/>: the byte
    /// budget spread over the clip, capped at 4 Mbit/s for short ones, and the height stepped down as the bitrate falls.
    /// </summary>
    public static (int VideoBps, int Height) WhatsAppPlan(double outSeconds, int sourceHeight)
    {
        var seconds = Math.Max(0.5, outSeconds);
        var budget = WhatsAppTargetBytes * 8.0 / seconds - WhatsAppAudioBps;
        var vbps = (int)Math.Clamp(budget, 60_000, 4_000_000);
        var ladder = vbps >= 2_500_000 ? 1080 : vbps >= 1_000_000 ? 720 : vbps >= 450_000 ? 480 : 360;
        var height = sourceHeight > 0 ? Math.Min(ladder, sourceHeight) : ladder;
        return (vbps, height);
    }

    /// <summary>Output height of a preset for a source of the given size; used for the caption size and the GIF shape.</summary>
    public static int OutputHeight(string preset, double outSeconds, int sourceWidth, int sourceHeight)
    {
        var even = (int)Math.Max(2, sourceHeight / 2 * 2);
        switch (preset)
        {
            case WhatsApp: return Math.Max(2, WhatsAppPlan(outSeconds, sourceHeight).Height / 2 * 2);
            case Reel: return ReelHeight;
            case YouTube: return Math.Min(1080, even);
            case Gif:
                var w = Math.Min(GifWidth, Math.Max(16, sourceWidth));
                return (int)Math.Max(2, Math.Round(w * (double)Math.Max(1, sourceHeight) / Math.Max(1, sourceWidth)));
            default: return even;
        }
    }

    /// <summary>Estimated size in bytes of the output. <paramref name="sourceBytes"/> and <paramref name="sourceSeconds"/> describe the input file.</summary>
    public static long EstimateBytes(string preset, double outSeconds, int sourceWidth, int sourceHeight, double sourceSeconds, long sourceBytes)
    {
        var seconds = Math.Max(0.1, outSeconds);
        double bytes;
        switch (preset)
        {
            case WhatsApp:
            {
                var (vbps, _) = WhatsAppPlan(seconds, sourceHeight);
                bytes = (vbps + WhatsAppAudioBps) / 8.0 * seconds * 1.02;
                bytes = Math.Min(bytes, WhatsAppTargetBytes);
                break;
            }
            case Reel:
                bytes = (3_500_000 + 128_000) / 8.0 * seconds;
                break;
            case YouTube:
                bytes = (6_000_000 + 160_000) / 8.0 * seconds;
                break;
            case Gif:
            {
                var h = OutputHeight(Gif, seconds, sourceWidth, sourceHeight);
                var w = Math.Min(GifWidth, Math.Max(16, sourceWidth));
                bytes = seconds * GifFps * w * h * 0.1;
                break;
            }
            default:
                bytes = sourceSeconds > 0 ? sourceBytes * (seconds / sourceSeconds) : sourceBytes;
                break;
        }
        return (long)Math.Max(1, Math.Round(bytes));
    }
}
