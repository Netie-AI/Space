using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>One highlight/mark posted by the pdf.js overlay (PDF points, origin bottom-left).</summary>
public sealed record PdfJsHighlight(
    string Id, int Page, double XPt, double YFromBottom, double WidthPt, double HeightPt, string Kind);

/// <summary>Resolve local pdf.js Netie viewer URL for WebView2.</summary>
public static class PdfJsViewerService
{
    public static string? FindViewerHtml()
    {
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "Assets", "pdfjs", "netie-viewer.html"),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "src", "NetieSpace", "Assets", "pdfjs", "netie-viewer.html"),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "Assets", "pdfjs", "netie-viewer.html"),
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }

    public static Uri? BuildViewerUri(string pdfPath)
    {
        var viewer = FindViewerHtml();
        if (viewer is null || !IoFile.Exists(pdfPath))
            return null;

        var fileUri = new Uri(pdfPath).AbsoluteUri;
        var viewerUri = new Uri(viewer).AbsoluteUri;
        return new Uri($"{viewerUri}?file={Uri.EscapeDataString(fileUri)}");
    }

    public static bool TryParseMessage(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read one highlight object or a <c>highlights</c> array from a viewer message.
    /// Prefers <c>yFromBottom</c>; falls back to top-left <c>y</c> + <c>pageHeightPt</c>.
    /// </summary>
    public static IReadOnlyList<PdfJsHighlight> ReadHighlights(JsonElement root)
    {
        var list = new List<PdfJsHighlight>();
        if (root.TryGetProperty("highlights", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (TryReadHighlight(el, out var h))
                    list.Add(h);
            }
            return list;
        }

        if (TryReadHighlight(root, out var one))
            list.Add(one);
        return list;
    }

    public static bool TryReadHighlight(JsonElement el, out PdfJsHighlight highlight)
    {
        highlight = default!;
        if (el.ValueKind != JsonValueKind.Object)
            return false;

        var page = (int)Math.Round(Num(el, "page"));
        if (page < 1)
            return false;

        var w = Num(el, "wPt", "w");
        var h = Num(el, "hPt", "h");
        if (w <= 0.5 || h <= 0.5)
            return false;

        var x = Num(el, "xPt", "x");
        var yFromBottom = ReadYFromBottom(el, h);
        var id = Str(el, "id") ?? "";
        var kind = Str(el, "kind");
        if (string.IsNullOrWhiteSpace(kind))
            kind = "highlight";

        highlight = new PdfJsHighlight(id, page, x, yFromBottom, w, h, kind);
        return true;
    }

    public static PdfHighlightItem ToEditItem(PdfJsHighlight h) =>
        new(h.Page, h.XPt, h.YFromBottom, h.WidthPt, h.HeightPt, h.Kind);

    /// <summary>Canvas top-left (px) → PDF points origin bottom-left. Shared with the viewer overlay.</summary>
    public static (double XPt, double YFromBottom, double WPt, double HPt) CanvasToPdfBottomLeft(
        double leftPx, double topPx, double widthPx, double heightPx,
        double canvasWidthPx, double canvasHeightPx,
        double pageWidthPt, double pageHeightPt)
    {
        if (canvasWidthPx <= 0 || canvasHeightPx <= 0)
            return (0, 0, 0, 0);

        var xPt = (leftPx / canvasWidthPx) * pageWidthPt;
        var wPt = (widthPx / canvasWidthPx) * pageWidthPt;
        var hPt = (heightPx / canvasHeightPx) * pageHeightPt;
        var yTopPt = (topPx / canvasHeightPx) * pageHeightPt;
        var yFromBottom = pageHeightPt - yTopPt - hPt;
        return (xPt, yFromBottom, wPt, hPt);
    }

    /// <summary>PDF bottom-left → PdfSharpCore XGraphics top-left Y.</summary>
    public static double PdfBottomLeftToXGraphicsTop(double pageHeightPt, double yFromBottom, double heightPt) =>
        pageHeightPt - yFromBottom - heightPt;

    private static double ReadYFromBottom(JsonElement el, double heightPt)
    {
        if (el.TryGetProperty("yFromBottom", out var yb) && yb.ValueKind == JsonValueKind.Number)
            return yb.GetDouble();

        var y = Num(el, "y");
        var pageH = Num(el, "pageHeightPt", "heightPt");
        if (pageH > 0)
            return pageH - y - heightPt;
        return y;
    }

    private static double Num(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
                return p.GetDouble();
        }
        return 0;
    }

    private static string? Str(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString();
        return null;
    }
}
