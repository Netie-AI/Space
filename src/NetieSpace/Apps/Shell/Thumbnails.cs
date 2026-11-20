using System.Windows.Media.Imaging;
using NetieSpace.Apps.Pdf.WinRt;
using NetieSpace.Helpers;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Small pictures of a document for the tool page: PDF pages from Windows.Data.Pdf (the renderer the
/// PDF tools already use), pictures from WPF's own decoder (WIC). Nothing new is shipped. A file the
/// engines cannot open returns null/empty and the page says so; it never throws into the UI.
/// </summary>
internal static class Thumbnails
{
    public sealed record PdfStrip(int PageCount, IReadOnlyList<BitmapSource> Pages);

    /// <summary>The first <paramref name="maxPages"/> pages at <paramref name="width"/> pixels wide.</summary>
    public static async Task<PdfStrip?> PdfPagesAsync(string path, int maxPages, int width, CancellationToken ct)
    {
        try
        {
            var doc = await WinPdfRender.OpenAsync(path, ct);
            var count = (int)doc.PageCount;
            var shown = Math.Min(count, maxPages);
            var pages = new List<BitmapSource>(shown);
            for (var i = 0; i < shown; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var page = doc.GetPage((uint)i);
                var ratio = page.Size.Height / Math.Max(1.0, page.Size.Width);
                var w = (uint)width;
                var h = (uint)Math.Clamp(Math.Round(width * ratio), 16, 4000);
                using var stream = await WinPdfRender.RenderAsync(page, w, h, Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, i + 1, path, ct);
                pages.Add(Decode(stream));
            }
            return new PdfStrip(count, pages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf thumbnails failed for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>A picture decoded to at most <paramref name="maxSide"/> pixels on its long side; null when WPF cannot read it.</summary>
    public static (BitmapSource Image, int PixelWidth, int PixelHeight)? Picture(string path, int maxSide)
    {
        try
        {
            // First pass reads only the size; the second decodes at the reduced size so a 40 MP photo stays cheap.
            int w, h;
            using (var s = IoFile.OpenRead(path))
            {
                var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(s, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                w = frame.PixelWidth;
                h = frame.PixelHeight;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(IoPath.GetFullPath(path));
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (w >= h) bmp.DecodePixelWidth = Math.Min(w, maxSide); else bmp.DecodePixelHeight = Math.Min(h, maxSide);
            bmp.EndInit();
            bmp.Freeze();
            return (bmp, w, h);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"picture preview failed for {path}: {ex.Message}");
            return null;
        }
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
}
