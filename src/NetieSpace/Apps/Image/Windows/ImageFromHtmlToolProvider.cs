using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Pdf.WinRt;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Image.WinRt;

/// <summary>
/// img-html-to-image: a local .html/.htm file captured to PNG or JPEG by a headless WebView2. The view is
/// sized to the document height (capped) for a full-page capture. Nothing that is not file:// is loaded.
/// Compiled only for the Windows TFM.
/// </summary>
public sealed class ImageFromHtmlToolProvider : IToolProvider
{
    public const int MaxHeight = 16384;
    private const int ViewportHeight = 800;

    public ToolApp App => ToolApp.Image;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("img-html-to-image", (r, p, ct) => Task.Run(() => RunAsync(r, p, ct), ct));
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var width = PdfToolSupport.OptInt(request, "width", 1280, 200, 4000);
        var format = PdfToolSupport.OptChoice(request, "format", "png", "png", "jpg");
        var fullPage = PdfToolSupport.OptBool(request, "full_page", true);
        if (request.Files.Count != 1)
            throw new ArgumentException("img-html-to-image takes exactly one .html or .htm file");

        var input = IoPath.GetFullPath(request.Files[0]);
        HtmlInput.Check(input);

        IoDirectory.CreateDirectory(request.OutDir);
        var temp = IoPath.Combine(request.OutDir, ".netie-html-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? final = null;
        var capped = false;
        try
        {
            progress?.Report(new ToolProgress(10, "loading " + IoPath.GetFileName(input)));
            var bytes = await WebView2Host.RunAsync(input, width, ViewportHeight, async s =>
            {
                var height = ViewportHeight;
                if (fullPage)
                {
                    // Sized to the document, twice, because a page may lay out differently at its own height.
                    for (var pass = 0; pass < 2; pass++)
                    {
                        var json = await s.Core.ExecuteScriptAsync(
                            "Math.max(document.documentElement.scrollHeight||0,(document.body&&document.body.scrollHeight)||0)");
                        if (!double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var measured))
                            throw new InvalidOperationException("cannot measure the height of " + input + " (got " + json + ")");
                        var wanted = (int)Math.Ceiling(measured);
                        if (wanted > MaxHeight) { wanted = MaxHeight; capped = true; }
                        wanted = Math.Max(wanted, 1);
                        if (wanted == height && pass > 0) break;
                        height = wanted;
                        await s.ResizeAsync(width, height);
                    }
                }
                using var ms = new System.IO.MemoryStream();
                await s.Core.CapturePreviewAsync(
                    format == "png" ? CoreWebView2CapturePreviewImageFormat.Png : CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
                return ms.ToArray();
            }, ct);

            if (bytes.Length < 100)
                throw new InvalidOperationException("the web view captured nothing for " + input);
            IoFile.WriteAllBytes(temp, bytes);

            final = WinPdfRender.FreePath(request.OutDir, IoPath.GetFileNameWithoutExtension(input), "." + format, Array.Empty<string>());
            IoFile.Move(temp, final);
            progress?.Report(new ToolProgress(100,
                "blocked " + WebView2Host.LastBlockedCount + " requests that were not file://"
                + (capped ? "; page is taller than " + MaxHeight + " px and was cut there" : "")));
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
