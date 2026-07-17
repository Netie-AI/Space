using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

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
}
