using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// Shared page rendering for the tools that start from a Windows.Data.Pdf page. Compiled only for the
/// Windows TFM, so Windows CI is its only proof.
/// </summary>
internal static class WinPdfRender
{
    // Windows.Data.Pdf reports page sizes in 96-per-inch units.
    public const double PageUnitsPerInch = 96.0;
    public const int MaxSidePixels = 20000;

    public static async Task<PdfDocument> OpenAsync(string input, CancellationToken ct)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(IoPath.GetFullPath(input)).AsTask(ct);
            return await PdfDocument.LoadFromFileAsync(file).AsTask(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("cannot open " + input + " as a PDF: " + ex.Message, ex);
        }
    }

    /// <summary>Pixel size of a page at the given dots per inch; throws when it is empty or absurdly large.</summary>
    public static (uint Width, uint Height) PixelSize(PdfPage page, int dpi, string input, int pageNumber)
    {
        var width = (uint)Math.Round(page.Size.Width / PageUnitsPerInch * dpi);
        var height = (uint)Math.Round(page.Size.Height / PageUnitsPerInch * dpi);
        if (width == 0 || height == 0 || width > MaxSidePixels || height > MaxSidePixels)
            throw new InvalidOperationException(
                $"page {pageNumber} of {input} would be {width}x{height} pixels at {dpi} dpi; lower option 'dpi'");
        return (width, height);
    }

    /// <summary>Renders one page onto white paper into a new stream positioned at 0.</summary>
    public static async Task<InMemoryRandomAccessStream> RenderAsync(
        PdfPage page, uint width, uint height, Guid encoderId, int pageNumber, string input, CancellationToken ct)
    {
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = width,
            DestinationHeight = height,
            BitmapEncoderId = encoderId,
            // Without a paper colour a transparent page comes out black in JPEG and confuses OCR.
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await page.RenderToStreamAsync(stream, options).AsTask(ct);
            if (stream.Size == 0)
                throw new InvalidOperationException($"page {pageNumber} of {input} rendered to nothing");
            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>A path in the folder that neither exists nor was handed out in this run.</summary>
    public static string FreePath(string dir, string stem, string ext, ICollection<string> taken)
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

    public static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try { IoFile.Delete(path); } catch { /* ignore */ }
        }
    }
}
