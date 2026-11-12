using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using Windows.Graphics.Imaging;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// pdf-to-powerpoint: one slide per page, the page drawn by the Windows PDF renderer as a picture.
/// The text is not editable. Compiled only for the Windows TFM.
/// </summary>
public sealed class PdfToPowerPointToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-to-powerpoint", RunAsync);
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var dpi = 150;
        if (request.Options.TryGetValue("dpi", out var dpiText) && !int.TryParse(dpiText, out dpi))
            throw new ArgumentException("option 'dpi': '" + dpiText + "' is not a number");
        if (dpi < 72 || dpi > 300)
            throw new ArgumentException("option 'dpi': " + dpi + " is outside 72..300");
        var format = request.Options.TryGetValue("image", out var f) ? f.ToLowerInvariant() : "jpg";
        if (format != "jpg" && format != "png")
            throw new ArgumentException("option 'image': '" + f + "' is not jpg or png");
        var png = format == "png";

        var outputs = new List<string>();
        try
        {
            for (var n = 0; n < request.Files.Count; n++)
            {
                var input = IoPath.GetFullPath(request.Files[n]);
                var document = await WinPdfRender.OpenAsync(input, ct);
                var total = (int)document.PageCount;
                if (total == 0)
                    throw new InvalidOperationException("no pages in " + input);

                var stem = IoPath.GetFileNameWithoutExtension(input);
                var outPath = WinPdfRender.FreePath(request.OutDir, stem, ".pptx", outputs);
                // The path is reserved before the file exists so a failure below can remove the partial file.
                outputs.Add(outPath);

                (long Width, long Height) size;
                using (var first = document.GetPage(0))
                    size = PptxDeck.SlideSizeFor(first.Size.Width, first.Size.Height);

                using (var deck = new PptxDeck(outPath, size.Width, size.Height))
                {
                    for (var i = 0; i < total; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var page = document.GetPage((uint)i);
                        var (w, h) = WinPdfRender.PixelSize(page, dpi, input, i + 1);
                        using var stream = await WinPdfRender.RenderAsync(
                            page, w, h, png ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId,
                            i + 1, input, ct);
                        using var source = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
                        deck.AddPicture(source, png, (int)w, (int)h, $"Page {i + 1} of {stem}");
                        progress?.Report(new ToolProgress(
                            (int)((n + (i + 1) / (double)total) * 100 / request.Files.Count),
                            $"page {i + 1} of {total}"));
                    }
                }
            }
        }
        catch
        {
            // Never leave half a deck behind looking like a result.
            WinPdfRender.DeleteQuietly(outputs);
            throw;
        }
        return outputs;
    }
}
