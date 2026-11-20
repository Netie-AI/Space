using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// pdf-from-html: a local .html/.htm file printed to PDF by a headless WebView2 (Chromium print engine).
/// Nothing that is not file:// is loaded. Compiled only for the Windows TFM.
/// </summary>
public sealed class PdfFromHtmlToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-from-html", (r, p, ct) => Task.Run(() => RunAsync(r, p, ct), ct));
    }

    private static (double W, double H) PaperInches(string size) => size switch
    {
        "letter" => (8.5, 11.0),
        "legal" => (8.5, 14.0),
        _ => (8.27, 11.69),
    };

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var size = PdfToolSupport.OptChoice(request, "size", "a4", "a4", "letter", "legal");
        var orientation = PdfToolSupport.OptChoice(request, "orientation", "portrait", "portrait", "landscape");
        var marginMm = PdfToolSupport.OptInt(request, "margin", 10, 0, 50);
        var background = PdfToolSupport.OptBool(request, "background", true);
        if (request.Files.Count != 1)
            throw new ArgumentException("pdf-from-html takes exactly one .html or .htm file");

        var input = IoPath.GetFullPath(request.Files[0]);
        HtmlInput.Check(input);

        var (w, h) = PaperInches(size);
        // Width and height are given already turned for landscape, with the engine left on portrait.
        if (orientation == "landscape") (w, h) = (h, w);
        var margin = marginMm / 25.4;

        IoDirectory.CreateDirectory(request.OutDir);
        var temp = IoPath.Combine(request.OutDir, ".netie-html-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? final = null;
        try
        {
            progress?.Report(new ToolProgress(10, "loading " + IoPath.GetFileName(input)));
            await WebView2Host.RunAsync(input, 1024, 768, async s =>
            {
                var settings = s.Environment.CreatePrintSettings();
                settings.Orientation = Microsoft.Web.WebView2.Core.CoreWebView2PrintOrientation.Portrait;
                settings.PageWidth = w;
                settings.PageHeight = h;
                settings.MarginTop = margin;
                settings.MarginBottom = margin;
                settings.MarginLeft = margin;
                settings.MarginRight = margin;
                settings.ShouldPrintBackgrounds = background;
                settings.ShouldPrintHeaderAndFooter = false;
                settings.ShouldPrintSelectionOnly = false;
                var ok = await s.Core.PrintToPdfAsync(temp, settings);
                if (!ok) throw new InvalidOperationException("the web view could not print " + input + " to PDF");
                return 0;
            }, ct);

            var info = new System.IO.FileInfo(temp);
            if (!info.Exists || info.Length < 100)
                throw new InvalidOperationException("the web view wrote no PDF for " + input);
            using (var fs = IoFile.OpenRead(temp))
            {
                var head = new byte[5];
                if (fs.Read(head, 0, 5) != 5 || System.Text.Encoding.ASCII.GetString(head) != "%PDF-")
                    throw new InvalidOperationException("the web view wrote something that is not a PDF for " + input);
            }

            final = WinPdfRender.FreePath(request.OutDir, IoPath.GetFileNameWithoutExtension(input), ".pdf", Array.Empty<string>());
            IoFile.Move(temp, final);
            progress?.Report(new ToolProgress(100, "blocked " + WebView2Host.LastBlockedCount + " requests that were not file://"));
            return new[] { final };
        }
        catch
        {
            WinPdfRender.DeleteQuietly(new[] { temp });
            if (final is not null) WinPdfRender.DeleteQuietly(new[] { final });
            throw;
        }
    }
}

/// <summary>Input checks shared by the two HTML tools.</summary>
internal static class HtmlInput
{
    public static void Check(string input)
    {
        var ext = IoPath.GetExtension(input).ToLowerInvariant();
        if (ext != ".html" && ext != ".htm")
            throw new ArgumentException(IoPath.GetFileName(input) + " is not an .html or .htm file");
        if (!IoFile.Exists(input))
            throw new System.IO.FileNotFoundException("no such file: " + input);
        if (new System.IO.FileInfo(input).Length == 0)
            throw new InvalidOperationException(input + " is empty");
    }
}
