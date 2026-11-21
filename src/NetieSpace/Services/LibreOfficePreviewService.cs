using System.Diagnostics;
using System.IO;
using System.Text;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Headless LibreOffice PPTX → PDF/PNG when PowerPoint COM is unavailable.
/// Cache: %LOCALAPPDATA%\NetieSpace\pptx-slides\{key}\
/// </summary>
public static class LibreOfficePreviewService
{
    public static string? TryExportSlidesHtml(string pptxPath)
    {
        if (string.IsNullOrWhiteSpace(pptxPath) || !IoFile.Exists(pptxPath))
            return null;

        var soffice = FindSoffice();
        if (soffice is null)
            return null;

        var fi = new FileInfo(pptxPath);
        var cacheDir = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "pptx-slides",
            PreviewHtmlDiskCache.CacheKey(pptxPath, fi.Length, fi.LastWriteTimeUtc) + "_lo");

        var htmlPath = IoPath.Combine(cacheDir, "deck.html");
        if (IoFile.Exists(htmlPath))
            return htmlPath;

        try
        {
            Directory.CreateDirectory(cacheDir);

            // Prefer PNG page export for scroll-snap slides; PDF fallback for WebView2
            if (TryConvert(soffice, pptxPath, cacheDir, "png"))
            {
                var pngs = Directory.GetFiles(cacheDir, "*.png")
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (pngs.Count > 0)
                {
                    WritePngDeckHtml(htmlPath, pngs);
                    return htmlPath;
                }
            }

            if (TryConvert(soffice, pptxPath, cacheDir, "pdf"))
            {
                var pdf = Directory.GetFiles(cacheDir, "*.pdf").FirstOrDefault();
                if (pdf is not null && IoFile.Exists(pdf))
                {
                    // WebView2 can open PDF via file://
                    WritePdfRedirectHtml(htmlPath, pdf);
                    return htmlPath;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"LibreOffice PPTX export failed: {ex.Message}");
            try
            {
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
            }
            catch { /* ignore */ }
            return null;
        }
    }

    public static string? FindSoffice()
    {
        var candidates = new[]
        {
            IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "LibreOffice", "program", "soffice.exe"),
            IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "LibreOffice", "program", "soffice.exe"),
            IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "LibreOffice", "program", "soffice.exe"),
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
        };

        foreach (var c in candidates)
        {
            if (IoFile.Exists(c))
                return c;
        }

        // PATH
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var hit = IoPath.Combine(dir.Trim('"'), "soffice.exe");
                if (IoFile.Exists(hit))
                    return hit;
            }
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>Open PPTX/ODP in the installed LibreOffice GUI (not headless). OnlyOffice AGPL stays banned.</summary>
    public static bool TryOpenForEdit(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
            return false;
        var soffice = FindSoffice();
        if (soffice is null)
            return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = soffice,
                Arguments = "\"" + path.Replace("\"", "") + "\"",
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"LibreOffice edit launch failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryConvert(string soffice, string pptxPath, string outDir, string format)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = soffice,
                Arguments = $"--headless --norestore --nolockcheck --nodefault --convert-to {format} --outdir \"{outDir}\" \"{pptxPath}\"",
                WorkingDirectory = outDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return false;

            if (!proc.WaitForExit(120_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                AppLogger.Warn("LibreOffice convert timed out");
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"LibreOffice --convert-to {format} failed: {ex.Message}");
            return false;
        }
    }

    private static void WritePngDeckHtml(string htmlPath, List<string> pngs)
    {
        var imgs = new StringBuilder();
        var count = pngs.Count;
        for (var i = 0; i < count; i++)
        {
            var name = IoPath.GetFileName(pngs[i]);
            imgs.Append("<section class='slide' id='s")
                .Append(i + 1)
                .Append("'><div class='slide-num'>Slide ")
                .Append(i + 1)
                .Append(" / ")
                .Append(count)
                .Append("</div><img src=\"")
                .Append(name.Replace("\"", "%22"))
                .Append("\" alt=\"Slide ")
                .Append(i + 1)
                .Append("\"/></section>");
        }

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
            "</div><div class=\"hint\">LibreOffice · PgUp / PgDn</div></body></html>";

        IoFile.WriteAllText(htmlPath, html, Encoding.UTF8);
    }

    private static void WritePdfRedirectHtml(string htmlPath, string pdfPath)
    {
        // Meta-refresh / JS navigate so HtmlFilePath stays deck.html next to PDF
        var name = IoPath.GetFileName(pdfPath);
        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
            $"<meta http-equiv=\"refresh\" content=\"0;url={name}\"/>" +
            $"<script>location.replace('{name.Replace("'", "%27")}');</script>" +
            "</head><body style='background:#0c1018;color:#ccc;font:14px sans-serif;padding:24px'>" +
            "Opening PDF preview…</body></html>";
        IoFile.WriteAllText(htmlPath, html, Encoding.UTF8);
    }
}
