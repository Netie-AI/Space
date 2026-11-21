using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Pdf.WinRt;
using NetieSpace.Helpers;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>
/// Pictures of PDF pages for the panels: Windows.Data.Pdf through <see cref="WinPdfRender"/>, the same renderer the
/// tool page strip uses. The page count that matters (the one the plan is written against) comes from the engine's own
/// reader, so a file the renderer cannot draw is still all there in the plan, just without pictures.
/// </summary>
internal static class PdfThumbs
{
    /// <summary>Pages drawn per file; the rest stay in the plan as numbered placeholders.</summary>
    public const int MaxDrawn = 300;

    /// <summary>Pages of the file as the engine will read them; 0 with the reason when it cannot be opened.</summary>
    public static Task<(int Pages, string? Problem)> CountAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                using var doc = PdfToolSupport.OpenImport(path);
                return (doc.PageCount, (string?)null);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"pdf panel: cannot read {path}: {ex.Message}");
                return (0, ex.Message);
            }
        });

    /// <summary>
    /// Draws pages 1..N of <paramref name="path"/> at <paramref name="width"/> pixels and hands each to
    /// <paramref name="onPage"/> on the caller's thread (index is 0-based). A page that does not draw is skipped.
    /// Returns false when the renderer could not open the file at all.
    /// </summary>
    public static async Task<bool> RenderAllAsync(string path, int width, Action<int, BitmapSource, bool> onPage, CancellationToken ct)
    {
        PdfDocument doc;
        try
        {
            doc = await WinPdfRender.OpenAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf panel: no pictures for {path}: {ex.Message}");
            return false;
        }

        var count = (int)Math.Min(doc.PageCount, MaxDrawn);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var bmp = await DrawAsync(doc, i, width, path, ct);
                onPage(i, bmp, IsBlank(bmp));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"pdf panel: page {i + 1} of {path} did not draw: {ex.Message}");
            }
        }
        return true;
    }

    /// <summary>One page, 0-based, at the given width; null when it does not draw.</summary>
    public static async Task<BitmapSource?> RenderPageAsync(string path, int index, int width, CancellationToken ct)
    {
        try
        {
            var doc = await WinPdfRender.OpenAsync(path, ct);
            if (index < 0 || index >= doc.PageCount) return null;
            return await DrawAsync(doc, index, width, path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf panel: page {index + 1} of {path} did not draw: {ex.Message}");
            return null;
        }
    }

    /// <summary>A page picture with the page's size in points (what the engine measures), for placing marks on it.</summary>
    public static async Task<(BitmapSource Image, double WidthPt, double HeightPt)?> RenderSizedAsync(string path, int index, int width, CancellationToken ct)
    {
        try
        {
            var doc = await WinPdfRender.OpenAsync(path, ct);
            if (index < 0 || index >= doc.PageCount) return null;
            double w, h;
            using (var page = doc.GetPage((uint)index))
            {
                w = page.Size.Width * 72.0 / WinPdfRender.PageUnitsPerInch;
                h = page.Size.Height * 72.0 / WinPdfRender.PageUnitsPerInch;
            }
            return (await DrawAsync(doc, index, width, path, ct), w, h);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf panel: page {index + 1} of {path} did not draw: {ex.Message}");
            return null;
        }
    }

    private static async Task<BitmapSource> DrawAsync(PdfDocument doc, int index, int width, string path, CancellationToken ct)
    {
        using var page = doc.GetPage((uint)index);
        var ratio = page.Size.Height / Math.Max(1.0, page.Size.Width);
        var h = (uint)Math.Clamp(Math.Round(width * ratio), 16, 4000);
        using var stream = await WinPdfRender.RenderAsync(page, (uint)width, h, Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, index + 1, path, ct);
        return Decode(stream);
    }

    private static BitmapSource Decode(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var net = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = net;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static bool IsBlank(BitmapSource bmp)
    {
        try
        {
            var f = bmp.Format == PixelFormats.Bgra32 ? (BitmapSource)bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var stride = f.PixelWidth * 4;
            var pixels = new byte[stride * f.PixelHeight];
            f.CopyPixels(pixels, stride, 0);
            return PdfBlankPage.IsBlank(pixels, f.PixelWidth, f.PixelHeight, stride);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf panel: blank check failed: {ex.Message}");
            return false;
        }
    }
}
