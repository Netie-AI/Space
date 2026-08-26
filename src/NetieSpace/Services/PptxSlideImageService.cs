using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Renders PPTX slides as real page images (PowerPoint COM when installed).
/// Returns null so caller can fall back to HTML text preview.
/// Also resolves the PNG for the slide currently on screen so chat can attach
/// that page — not a screenshot of the whole deck.
/// </summary>
public static class PptxSlideImageService
{
    /// <summary>1-based index of the scroll-snapped slide. Safe on cached HTML that never got the tracker script.</summary>
    public const string VisibleSlideIndexScript =
        "(function(){try{" +
        "if(typeof window.__netieVisibleSlideIndex==='function'){var n=window.__netieVisibleSlideIndex();if(n>0)return String(n);}" +
        "if(typeof window.__netieSlideIndex==='number'&&window.__netieSlideIndex>0)return String(window.__netieSlideIndex);" +
        "var deck=document.getElementById('deck');" +
        "var slides=deck?deck.querySelectorAll('.slide'):document.querySelectorAll('section.slide,.slide');" +
        "if(!slides||!slides.length)return '1';" +
        "var root=deck||document.scrollingElement||document.documentElement;" +
        "var mid=(root.scrollTop||0)+((root.clientHeight||window.innerHeight)/2);" +
        "var best=1,dist=1e12;" +
        "for(var i=0;i<slides.length;i++){var r=slides[i];var top=r.offsetTop,h=r.offsetHeight||0;var d=Math.abs(top+h/2-mid);if(d<dist){dist=d;best=i+1;}}" +
        "return String(best);}catch(e){return '1';}})()";

    /// <summary>PNG data URL of the visible slide's &lt;img&gt; — not CapturePreview of the whole WebView.</summary>
    public const string VisibleSlideDataUrlScript =
        "(function(){try{" +
        "var idx=1;" +
        "if(typeof window.__netieVisibleSlideIndex==='function'){var n=window.__netieVisibleSlideIndex();if(n>0)idx=n;}" +
        "else if(typeof window.__netieSlideIndex==='number'&&window.__netieSlideIndex>0)idx=window.__netieSlideIndex;" +
        "else {" +
        "var deck=document.getElementById('deck');" +
        "var slides=deck?deck.querySelectorAll('.slide'):document.querySelectorAll('section.slide,.slide');" +
        "if(slides&&slides.length){var root=deck||document.scrollingElement||document.documentElement;" +
        "var mid=(root.scrollTop||0)+((root.clientHeight||window.innerHeight)/2);" +
        "var best=1,dist=1e12;" +
        "for(var i=0;i<slides.length;i++){var r=slides[i];var top=r.offsetTop,h=r.offsetHeight||0;var d=Math.abs(top+h/2-mid);if(d<dist){dist=d;best=i+1;}}" +
        "idx=best;}" +
        "}" +
        "var img=null;" +
        "var deck2=document.getElementById('deck');" +
        "var nodes=deck2?deck2.querySelectorAll('.slide'):document.querySelectorAll('section.slide,.slide');" +
        "if(nodes&&nodes.length){var s=nodes[Math.max(0,Math.min(idx-1,nodes.length-1))];img=s.querySelector?s.querySelector('img'):null;}" +
        "if(!img){var vis=document.querySelectorAll('img');for(var j=0;j<vis.length;j++){var b=vis[j].getBoundingClientRect();if(b.width>40&&b.height>40&&b.top<window.innerHeight&&b.bottom>0){img=vis[j];break;}}}" +
        "if(!img)return '';" +
        "var c=document.createElement('canvas');c.width=img.naturalWidth||img.width;c.height=img.naturalHeight||img.height;" +
        "if(!c.width||!c.height)return '';" +
        "c.getContext('2d').drawImage(img,0,0);return c.toDataURL('image/png');" +
        "}catch(e){return '';}})()";

    public static string? TryExportSlidesHtml(string pptxPath, int width = 1280, int height = 720)
    {
        if (string.IsNullOrWhiteSpace(pptxPath) || !IoFile.Exists(pptxPath))
            return null;

        var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
        if (pptType is null)
            return null;

        var cacheDir = ComCacheDir(pptxPath);
        if (string.IsNullOrWhiteSpace(cacheDir))
            return null;

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
                "<script>" + DeckTrackerScript + "</script></body></html>";

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

    /// <summary>
    /// Resolve the exported PNG for one 1-based slide. Prefers PowerPoint COM
    /// <c>slide_NNN.png</c>, then LibreOffice siblings, then PNGs next to the preview HTML.
    /// </summary>
    public static string? TryGetSlidePng(string pptxPath, int slideIndex1Based, string? htmlFilePath = null)
    {
        if (slideIndex1Based < 1 || string.IsNullOrWhiteSpace(pptxPath))
            return null;

        var com = ComCacheDir(pptxPath);
        if (!string.IsNullOrWhiteSpace(com))
        {
            var hit = TryGetSlidePngInDir(com, slideIndex1Based);
            if (hit is not null)
                return hit;
        }

        var lo = LibreOfficeCacheDir(pptxPath);
        if (!string.IsNullOrWhiteSpace(lo))
        {
            var hit = TryGetSlidePngInDir(lo, slideIndex1Based);
            if (hit is not null)
                return hit;
        }

        if (!string.IsNullOrWhiteSpace(htmlFilePath))
        {
            var dir = IoPath.GetDirectoryName(htmlFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var hit = TryGetSlidePngInDir(dir, slideIndex1Based);
                if (hit is not null)
                    return hit;
            }
        }

        return null;
    }

    /// <summary>Copy the slide PNG into chat-clips so a later cache prune cannot drop the attachment.</summary>
    public static string? TryCopySlidePngForChat(string pptxPath, int slideIndex1Based, string destDir, string? htmlFilePath = null)
    {
        var src = TryGetSlidePng(pptxPath, slideIndex1Based, htmlFilePath);
        if (src is null || !IoFile.Exists(src))
            return null;

        try
        {
            IoDirectory.CreateDirectory(destDir);
            var dest = IoPath.Combine(destDir, $"slide_{slideIndex1Based:D3}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            IoFile.Copy(src, dest, overwrite: true);
            return dest;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Slide PNG copy failed: {ex.Message}");
            return src;
        }
    }

    public static string? TryWritePngFromDataUrl(string? dataUrl, string destPath)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) ||
            !dataUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return null;

        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || comma >= dataUrl.Length - 1)
            return null;

        try
        {
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            if (bytes.Length < 32)
                return null;
            var dir = IoPath.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(dir))
                IoDirectory.CreateDirectory(dir);
            IoFile.WriteAllBytes(destPath, bytes);
            return destPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Slide data-URL write failed: {ex.Message}");
            return null;
        }
    }

    public static bool IsPptxPath(string? path)
    {
        var ext = IoPath.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".pptx" or ".ppsx" or ".ppt";
    }

    private static string? TryGetSlidePngInDir(string dir, int slideIndex1Based)
    {
        if (string.IsNullOrWhiteSpace(dir) || !IoDirectory.Exists(dir))
            return null;

        var padded = IoPath.Combine(dir, $"slide_{slideIndex1Based:D3}.png");
        if (IoFile.Exists(padded))
            return padded;

        var plain = IoPath.Combine(dir, $"slide_{slideIndex1Based}.png");
        if (IoFile.Exists(plain))
            return plain;

        string[] pngs;
        try
        {
            pngs = IoDirectory.GetFiles(dir, "*.png")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return null;
        }

        if (slideIndex1Based <= pngs.Length)
            return pngs[slideIndex1Based - 1];
        return null;
    }

    private static string? ComCacheDir(string pptxPath)
    {
        try
        {
            var fi = new FileInfo(pptxPath);
            if (!fi.Exists)
                return null;
            return IoPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetieSpace", "pptx-slides",
                PreviewHtmlDiskCache.CacheKey(pptxPath, fi.Length, fi.LastWriteTimeUtc));
        }
        catch
        {
            return null;
        }
    }

    private static string? LibreOfficeCacheDir(string pptxPath)
    {
        var com = ComCacheDir(pptxPath);
        return string.IsNullOrWhiteSpace(com) ? null : com + "_lo";
    }

    /// <summary>Tracks the snapped slide and PgUp/PgDn. Also used by ExecuteScript fallbacks on older cached decks.</summary>
    private const string DeckTrackerScript =
        "(function(){" +
        "const deck=document.getElementById('deck');if(!deck)return;" +
        "const slides=deck.querySelectorAll('.slide');" +
        "function current(){let best=1,dist=1e12;const mid=deck.scrollTop+(deck.clientHeight/2);" +
        "for(let i=0;i<slides.length;i++){const t=slides[i].offsetTop,h=slides[i].offsetHeight||0,d=Math.abs(t+h/2-mid);if(d<dist){dist=d;best=i+1;}}" +
        "window.__netieSlideIndex=best;return best;}" +
        "window.__netieVisibleSlideIndex=current;" +
        "deck.addEventListener('scroll',current,{passive:true});current();" +
        "document.addEventListener('keydown',e=>{" +
        "const h=deck.clientHeight;" +
        "if(e.key==='PageDown'||e.key==='ArrowRight'){e.preventDefault();deck.scrollBy({top:h,behavior:'smooth'});}" +
        "else if(e.key==='PageUp'||e.key==='ArrowLeft'){e.preventDefault();deck.scrollBy({top:-h,behavior:'smooth'});}" +
        "});" +
        "})();";
}
