using System.Globalization;

namespace NetieSpace.Apps.Shell;

public enum PreviewKind { Pdf, Image, Other }

/// <summary>What the tool page shows beside the options, decided from the file name alone. No WPF.</summary>
public static class PreviewPlan
{
    /// <summary>Pages drawn in the thumbnail strip; the rest are counted, not drawn.</summary>
    public const int MaxPages = 12;

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico", ".heic",
    };

    public static PreviewKind KindOf(string? path)
    {
        var ext = IoPath.GetExtension(path ?? "");
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            return PreviewKind.Pdf;
        return ImageTypes.Contains(ext) ? PreviewKind.Image : PreviewKind.Other;
    }

    /// <summary>"1 page", "12 pages", or "12 pages, first 12 shown" style caption.</summary>
    public static string PageCaption(int total, int shown)
    {
        var inv = CultureInfo.InvariantCulture;
        var pages = total == 1 ? "1 page" : total.ToString(inv) + " pages";
        return shown < total ? $"{pages} - first {shown.ToString(inv)} shown" : pages;
    }

    /// <summary>Upper-case extension for the placeholder tile ("MP4"); "FILE" when there is none.</summary>
    public static string TypeBadge(string? path)
    {
        var ext = IoPath.GetExtension(path ?? "").TrimStart('.');
        return ext.Length == 0 ? "FILE" : ext.ToUpperInvariant();
    }
}
