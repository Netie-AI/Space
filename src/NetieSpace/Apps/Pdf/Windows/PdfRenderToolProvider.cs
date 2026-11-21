using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// PDF tools that need the Windows PDF renderer (Windows.Data.Pdf). Compiled only for the
/// Windows TFM, so nothing here can run on the Linux gate: Windows CI is its only proof.
/// </summary>
public sealed class PdfRenderToolProvider : IToolProvider
{
    private const double PointsPerInch = 72.0;
    // Windows.Data.Pdf reports page sizes in 96-per-inch units.
    private const double PageUnitsPerInch = 96.0;
    private const int MaxSidePixels = 20000;

    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-to-jpg", ToJpgAsync);
    }

    /// <summary>One JPEG per page, named name-p001.jpg, at the chosen dots per inch.</summary>
    private static async Task<IReadOnlyList<string>> ToJpgAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var dpi = 150;
        if (request.Options.TryGetValue("dpi", out var dpiText) && !int.TryParse(dpiText, out dpi))
            throw new ArgumentException("option 'dpi': '" + dpiText + "' is not a number");
        if (dpi < 36 || dpi > 600)
            throw new ArgumentException("option 'dpi': " + dpi + " is outside 36..600");

        var outputs = new List<string>();
        try
        {
            for (var f = 0; f < request.Files.Count; f++)
            {
                var input = IoPath.GetFullPath(request.Files[f]);
                var file = await StorageFile.GetFileFromPathAsync(input);
                PdfDocument document;
                try
                {
                    document = await PdfDocument.LoadFromFileAsync(file);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("cannot open " + input + " as a PDF: " + ex.Message, ex);
                }

                var total = (int)document.PageCount;
                if (total == 0)
                    throw new InvalidOperationException("no pages in " + input);
                var stem = IoPath.GetFileNameWithoutExtension(input);

                for (var i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var page = document.GetPage((uint)i);
                    var width = (uint)Math.Round(page.Size.Width / PageUnitsPerInch * dpi);
                    var height = (uint)Math.Round(page.Size.Height / PageUnitsPerInch * dpi);
                    if (width == 0 || height == 0 || width > MaxSidePixels || height > MaxSidePixels)
                        throw new InvalidOperationException(
                            $"page {i + 1} of {input} would be {width}x{height} pixels at {dpi} dpi; lower option 'dpi'");

                    var options = new PdfPageRenderOptions
                    {
                        DestinationWidth = width,
                        DestinationHeight = height,
                        BitmapEncoderId = BitmapEncoder.JpegEncoderId,
                        // JPEG has no alpha: without a paper colour, transparent pages come out black.
                        BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                    };

                    var outPath = FreePath(request.OutDir, $"{stem}-p{i + 1:000}", ".jpg", outputs);
                    // Listed before the file exists: a failed or cancelled write leaves a partial file, and the
                    // rollback below can only delete what it has been told about.
                    outputs.Add(outPath);
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await page.RenderToStreamAsync(stream, options);
                        if (stream.Size == 0)
                            throw new InvalidOperationException($"page {i + 1} of {input} rendered to nothing");
                        stream.Seek(0);
                        using var source = stream.AsStreamForRead();
                        using var target = IoFile.Create(outPath);
                        await source.CopyToAsync(target, ct);
                    }
                    progress?.Report(new ToolProgress(
                        (int)((f + (i + 1) / (double)total) * 100 / request.Files.Count),
                        $"page {i + 1} of {total}"));
                }
            }
        }
        catch
        {
            // Never leave half a set behind looking like a result.
            foreach (var path in outputs)
            {
                try { IoFile.Delete(path); } catch { /* ignore */ }
            }
            throw;
        }

        return outputs;
    }

    /// <summary>A path in the folder that neither exists nor was handed out in this run.</summary>
    private static string FreePath(string dir, string stem, string ext, List<string> taken)
    {
        IoDirectory.CreateDirectory(dir);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? stem + ext : $"{stem}-{n}{ext}";
            var path = IoPath.Combine(dir, name);
            if (!IoFile.Exists(path) && !taken.Contains(path))
                return path;
        }
    }
}
