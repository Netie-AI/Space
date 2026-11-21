using NetieSpace.Models;

namespace NetieSpace.Helpers;

/// <summary>
/// Decisions for the peek window chrome that need no WPF, so xunit can pin them on Linux.
/// PreviewWindow calls these; it does not repeat the numbers.
/// </summary>
public static class PeekChromeRules
{
    /// <summary>Fit never enlarges: a 64 px picture opens at 100%, not 684%.</summary>
    public const double MaxFitZoom = 1.0;
    public const double MinFitZoom = 0.05;

    /// <summary>Summary strip height in lines for documents.</summary>
    public const int SummaryLines = 2;

    public static double FitZoom(double viewportW, double viewportH, double contentW, double contentH)
    {
        if (contentW < 1 || contentH < 1)
            return 1.0;
        var fit = Math.Min(Math.Max(1, viewportW) / contentW, Math.Max(1, viewportH) / contentH);
        return Math.Clamp(fit, MinFitZoom, MaxFitZoom);
    }

    /// <summary>Pictures and clips have no words to summarize: the strip stays hidden until asked.</summary>
    public static bool SummaryByDefault(PreviewKind kind) =>
        kind is not (PreviewKind.Image or PreviewKind.Video);

    /// <summary>Edit takes the primary slot only when the kind has no "Open in Netie ..." action.</summary>
    public static bool EditIsPrimary(bool openInAppShown) => !openInAppShown;

    /// <summary>The label shows the real zoom, rounded, never a clamped or stale number.</summary>
    public static string ZoomText(double zoom) => $"{Math.Round(zoom * 100):0}%";
}
