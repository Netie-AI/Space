using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>Quick Look–style metadata for folders, exe, and unknown binaries.</summary>
public static class ShellInfoPreview
{
    public static PreviewContent Build(string path)
    {
        var isDir = IoDirectory.Exists(path);
        var name = IoPath.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            name = path;

        DateTime created = default, modified = default;
        long size = 0;
        string typeLabel;
        string extra = "";

        if (isDir)
        {
            var di = new DirectoryInfo(path);
            created = di.CreationTime;
            modified = di.LastWriteTime;
            typeLabel = "Folder";
            try
            {
                var entries = di.EnumerateFileSystemInfos()
                    .OrderByDescending(e => e is DirectoryInfo)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(400)
                    .ToList();
                var files = entries.Count(e => e is FileInfo);
                var dirs = entries.Count(e => e is DirectoryInfo);
                long shallow = 0;
                foreach (var f in entries.OfType<FileInfo>().Take(200))
                    shallow += f.Length;
                extra = $"Items (shallow): {files} files, {dirs} folders<br/>Approx. size (first 200 files): {FormatSize(shallow)}";
                size = shallow;

                var list = new StringBuilder();
                list.Append("<div class='folder-list'><div class='folder-hint'>Double-click or press Enter on a row to preview · Space backs out</div>");
                foreach (var e in entries)
                {
                    var isSubDir = e is DirectoryInfo;
                    var childSize = e is FileInfo fi ? FormatSize(fi.Length) : "Folder";
                    var full = System.Net.WebUtility.HtmlEncode(e.FullName.Replace('\\', '/'));
                    var label = System.Net.WebUtility.HtmlEncode(e.Name);
                    list.Append(
                        $"<button type='button' class='folder-item' data-path=\"{full}\">" +
                        $"<span class='folder-ico'>{(isSubDir ? "📁" : "📄")}</span>" +
                        $"<span class='folder-name'>{label}</span>" +
                        $"<span class='folder-meta'>{childSize}</span></button>");
                }
                list.Append("</div>");
                extra += list.ToString();
            }
            catch (Exception ex)
            {
                extra = System.Net.WebUtility.HtmlEncode(ex.Message);
            }
        }
        else
        {
            var fi = new FileInfo(path);
            created = fi.CreationTime;
            modified = fi.LastWriteTime;
            size = fi.Length;
            var ext = fi.Extension.ToLowerInvariant();
            typeLabel = string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " file";

            if (ext is ".exe" or ".dll")
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var bits = new List<string>();
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                        bits.Add(System.Net.WebUtility.HtmlEncode(vi.FileDescription));
                    if (!string.IsNullOrWhiteSpace(vi.ProductName))
                        bits.Add("Product: " + System.Net.WebUtility.HtmlEncode(vi.ProductName));
                    if (!string.IsNullOrWhiteSpace(vi.FileVersion))
                        bits.Add("Version: " + System.Net.WebUtility.HtmlEncode(vi.FileVersion));
                    if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                        bits.Add("Company: " + System.Net.WebUtility.HtmlEncode(vi.CompanyName));
                    extra = string.Join("<br/>", bits);
                }
                catch
                {
                    // ignore
                }
            }
        }

        var iconDataUri = TryIconDataUri(path);
        var iconHtml = iconDataUri is not null
            ? $"<img class='shell-icon' src='{iconDataUri}' alt=''/>"
            : "<div class='shell-icon-fallback'></div>";

        var body = $"""
            <div class="shell-info">
              {iconHtml}
              <h1>{System.Net.WebUtility.HtmlEncode(name)}</h1>
              <p class="muted">{System.Net.WebUtility.HtmlEncode(typeLabel)}</p>
              <dl>
                <dt>Location</dt><dd>{System.Net.WebUtility.HtmlEncode(path)}</dd>
                <dt>Created</dt><dd>{created:yyyy-MM-dd HH:mm}</dd>
                <dt>Modified</dt><dd>{modified:yyyy-MM-dd HH:mm}</dd>
                <dt>Size</dt><dd>{(isDir ? "—" : FormatSize(size))}</dd>
              </dl>
              {(string.IsNullOrWhiteSpace(extra) ? "" : $"<div class='extra'>{extra}</div>")}
            </div>
            """;

        var html = $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"/>
            <style>
              body{margin:0;padding:48px 40px;background:#10141c;color:#f2f4f8;
                font:15px/1.6 "Segoe UI Variable Text","SF Pro Text",sans-serif}
              {{GlassScrollbarCss.StyleBlock}}
              .shell-info{max-width:640px;margin:0 auto;text-align:center}
              .shell-icon{width:96px;height:96px;object-fit:contain;margin-bottom:20px}
              .shell-icon-fallback{width:96px;height:96px;margin:0 auto 20px;border-radius:18px;
                background:linear-gradient(145deg,#3a4560,#1a2030);border:1px solid rgba(255,255,255,.12)}
              h1{font-size:22px;font-weight:600;margin:0 0 6px;word-break:break-word}
              .muted{color:#8a93a6;margin:0 0 28px}
              dl{text-align:left;margin:0;padding:18px 20px;border-radius:14px;
                background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.08)}
              dt{color:#8a93a6;font-size:11px;text-transform:uppercase;letter-spacing:.04em;margin-top:12px}
              dt:first-child{margin-top:0}
              dd{margin:4px 0 0;word-break:break-all}
              .extra{margin-top:20px;text-align:left;color:#c5cad6;font-size:13px;
                padding:14px 18px;border-radius:12px;background:rgba(255,255,255,.03)}
              .folder-list{margin-top:16px;max-height:42vh;overflow:auto;text-align:left;
                border-radius:12px;border:1px solid rgba(255,255,255,.1);padding:6px}
              .folder-hint{font-size:11px;color:#8a93a6;padding:6px 8px 10px}
              .folder-item{display:flex;align-items:center;gap:10px;width:100%;
                background:transparent;border:0;border-radius:8px;color:#f2f4f8;
                padding:8px 10px;cursor:pointer;font:13px/1.3 "Segoe UI Variable Text",sans-serif;text-align:left}
              .folder-item:hover,.folder-item:focus{background:rgba(94,196,184,.16);outline:none}
              .folder-ico{width:22px;flex:0 0 auto}
              .folder-name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
              .folder-meta{color:#8a93a6;font-size:11px;flex:0 0 auto}
            </style></head><body>{{body}}
            <script>
            (function(){
              function post(path){
                try{
                  if(window.chrome&&chrome.webview)
                    chrome.webview.postMessage(JSON.stringify({type:'openPath',path:path}));
                }catch(e){}
              }
              document.querySelectorAll('.folder-item').forEach(function(btn){
                btn.addEventListener('dblclick',function(){ post(btn.getAttribute('data-path')); });
                btn.addEventListener('keydown',function(ev){
                  if(ev.key==='Enter'){ ev.preventDefault(); post(btn.getAttribute('data-path')); }
                });
                btn.addEventListener('click',function(){ btn.focus(); });
              });
            })();
            </script>
            </body></html>
            """;

        return new PreviewContent
        {
            FilePath = path,
            FileName = name,
            Kind = PreviewKind.ShellInfo,
            FileSizeBytes = size,
            RawText = $"{name}\n{typeLabel}\n{path}",
            RenderedHtml = html
        };
    }

    private static string? TryIconDataUri(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}

/// <summary>Disk cache for Office HTML previews under %LOCALAPPDATA%\NetieSpace\preview-cache.</summary>
public static class PreviewHtmlDiskCache
{
    private static string CacheDir =>
        IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "preview-cache");

    public static string? TryRead(string filePath, long size, DateTime lastWriteUtc)
    {
        try
        {
            var path = TryGetHtmlFile(filePath, size, lastWriteUtc);
            if (path is null)
                return null;
            return IoFile.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns cached HTML file path if present (for WebView file:// navigation).</summary>
    public static string? TryGetHtmlFile(string filePath, long size, DateTime lastWriteUtc)
    {
        try
        {
            var key = Key(filePath, size, lastWriteUtc);
            // Bundle: {key}/preview.html next to media/
            var bundle = IoPath.Combine(CacheDir, key, "preview.html");
            if (IoFile.Exists(bundle))
                return bundle;

            var flat = IoPath.Combine(CacheDir, key + ".html");
            return IoFile.Exists(flat) ? flat : null;
        }
        catch
        {
            return null;
        }
    }

    public static string Write(string filePath, long size, DateTime lastWriteUtc, string html)
    {
        IoDirectory.CreateDirectory(CacheDir);
        var path = IoPath.Combine(CacheDir, Key(filePath, size, lastWriteUtc) + ".html");
        IoFile.WriteAllText(path, html);
        return path;
    }

    /// <summary>Bundle dir for HTML + sibling media/ (mammoth external images).</summary>
    public static string BundleDir(string filePath, long size, DateTime lastWriteUtc)
    {
        var dir = IoPath.Combine(CacheDir, Key(filePath, size, lastWriteUtc));
        IoDirectory.CreateDirectory(dir);
        return dir;
    }

    public static string BundleHtmlPath(string filePath, long size, DateTime lastWriteUtc) =>
        IoPath.Combine(BundleDir(filePath, size, lastWriteUtc), "preview.html");

    /// <summary>Copy engine HTML + optional sibling media/ into the disk-cache bundle.</summary>
    public static string ImportBundle(string filePath, long size, DateTime lastWriteUtc, string sourceHtmlPath)
    {
        var destDir = BundleDir(filePath, size, lastWriteUtc);
        var destHtml = IoPath.Combine(destDir, "preview.html");
        IoFile.Copy(sourceHtmlPath, destHtml, overwrite: true);

        var srcMedia = IoPath.Combine(IoPath.GetDirectoryName(sourceHtmlPath)!, "media");
        var destMedia = IoPath.Combine(destDir, "media");
        if (IoDirectory.Exists(srcMedia))
        {
            if (IoDirectory.Exists(destMedia))
            {
                try { IoDirectory.Delete(destMedia, true); } catch { /* ignore */ }
            }

            CopyDirectory(srcMedia, destMedia);
        }

        return destHtml;
    }

    private static void CopyDirectory(string src, string dest)
    {
        IoDirectory.CreateDirectory(dest);
        foreach (var file in IoDirectory.GetFiles(src))
            IoFile.Copy(file, IoPath.Combine(dest, IoPath.GetFileName(file)), overwrite: true);
        foreach (var dir in IoDirectory.GetDirectories(src))
            CopyDirectory(dir, IoPath.Combine(dest, IoPath.GetFileName(dir)));
    }

    public static string CacheKey(string filePath, long size, DateTime lastWriteUtc) =>
        Key(filePath, size, lastWriteUtc);

    private static string Key(string filePath, long size, DateTime lastWriteUtc)
    {
        var raw = $"{filePath}|{size}|{lastWriteUtc.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
