using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Renders PPTX slides as real page images (PowerPoint COM when installed).
/// Returns null so caller can fall back to HTML text preview.
/// </summary>
public static class PptxSlideImageService
{
    public static string? TryExportSlidesHtml(string pptxPath, int width = 1280, int height = 720)
    {
        if (string.IsNullOrWhiteSpace(pptxPath) || !IoFile.Exists(pptxPath))
            return null;

        var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
        if (pptType is null)
            return null;

        var fi = new FileInfo(pptxPath);
        var cacheDir = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "pptx-slides",
            PreviewHtmlDiskCache.CacheKey(pptxPath, fi.Length, fi.LastWriteTimeUtc));

        var htmlPath = IoPath.Combine(cacheDir, "deck.html");
        if (IoFile.Exists(htmlPath))
            return htmlPath;

        object? app = null;
        object? open = null;
        try
        {
            Directory.CreateDirectory(cacheDir);
            app = Activator.CreateInstance(pptType);
            if (app is null)
                return null;

            try
            {
                pptType.InvokeMember(
                    "Visible",
                    System.Reflection.BindingFlags.SetProperty,
                    null,
                    app,
                    new object[] { 0 });
            }
            catch
            {
                // older hosts
            }

            var presentations = pptType.InvokeMember(
                "Presentations",
                System.Reflection.BindingFlags.GetProperty,
                null,
                app,
                null);
            if (presentations is null)
                return null;

            open = presentations.GetType().InvokeMember(
                "Open",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                presentations,
                new object[] { pptxPath, true, false, false });

            if (open is null)
                return null;

            var slides = open.GetType().InvokeMember(
                "Slides",
                System.Reflection.BindingFlags.GetProperty,
                null,
                open,
                null);
            if (slides is null)
                return null;

            var count = Convert.ToInt32(slides.GetType().InvokeMember(
                "Count",
                System.Reflection.BindingFlags.GetProperty,
                null,
                slides,
                null));

            if (count < 1)
                return null;

            var imgs = new StringBuilder();
            for (var i = 1; i <= count; i++)
            {
                var slide = slides.GetType().InvokeMember(
                    "Item",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    slides,
                    new object[] { i });
                if (slide is null)
                    continue;

                var png = IoPath.Combine(cacheDir, $"slide_{i:D3}.png");
                slide.GetType().InvokeMember(
                    "Export",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    slide,
                    new object[] { png, "PNG", width, height });

                if (!IoFile.Exists(png))
                    continue;

                var b64 = Convert.ToBase64String(IoFile.ReadAllBytes(png));
                imgs.Append("<section class='slide' id='s")
                    .Append(i)
                    .Append("'><div class='slide-num'>Slide ")
                    .Append(i)
                    .Append(" / ")
                    .Append(count)
                    .Append("</div><img src=\"data:image/png;base64,")
                    .Append(b64)
                    .Append("\" alt=\"Slide ")
                    .Append(i)
                    .Append("\"/></section>");
            }

            if (imgs.Length == 0)
                return null;

            var html =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
                "<style>" +
                "html,body{margin:0;height:100%;background:#0c1018;color:#e8ecf4;" +
                "font:13px/1.4 \"Segoe UI Variable Text\",sans-serif}" +
                "body{overflow:hidden}" +
                "#deck{height:100%;overflow-y:auto;scroll-snap-type:y mandatory;" +
                "scroll-behavior:smooth;padding:12px 16px 28px;box-sizing:border-box}" +
                ".slide{scroll-snap-align:start;min-height:calc(100% - 8px);" +
                "display:flex;flex-direction:column;align-items:center;justify-content:center;margin:0 0 18px}" +
                ".slide-num{align-self:flex-start;opacity:.55;margin:0 0 8px 4px;font-size:11px}" +
                ".slide img{max-width:100%;max-height:calc(100vh - 48px);width:auto;height:auto;" +
                "border-radius:10px;box-shadow:0 12px 40px rgba(0,0,0,.45);background:#111}" +
                ".hint{position:fixed;right:14px;bottom:12px;opacity:.4;font-size:11px;pointer-events:none}" +
                "</style></head><body><div id=\"deck\">" +
                imgs +
                "</div><div class=\"hint\">PgUp / PgDn · scroll for slides</div>" +
                "<script>document.addEventListener('keydown',e=>{" +
                "const deck=document.getElementById('deck');const h=deck.clientHeight;" +
                "if(e.key==='PageDown'||e.key==='ArrowRight'){e.preventDefault();deck.scrollBy({top:h,behavior:'smooth'});}" +
                "else if(e.key==='PageUp'||e.key==='ArrowLeft'){e.preventDefault();deck.scrollBy({top:-h,behavior:'smooth'});}" +
                "});</script></body></html>";

            IoFile.WriteAllText(htmlPath, html, Encoding.UTF8);

            try
            {
                open.GetType().InvokeMember(
                    "Close",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    open,
                    null);
            }
            catch
            {
                // ignore
            }

            return htmlPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"PowerPoint slide export failed: {ex.Message}");
            try
            {
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
            }
            catch
            {
                // ignore
            }

            return null;
        }
        finally
        {
            try
            {
                if (app is not null)
                {
                    app.GetType().InvokeMember(
                        "Quit",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        app,
                        null);
                }
            }
            catch
            {
                // ignore
            }

            if (open is not null && Marshal.IsComObject(open))
                Marshal.FinalReleaseComObject(open);
            if (app is not null && Marshal.IsComObject(app))
                Marshal.FinalReleaseComObject(app);
        }
    }
}
