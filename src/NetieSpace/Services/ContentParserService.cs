using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using Markdig;
using NetieSpace.Helpers;
using NetieSpace.Models;
using UglyToad.PdfPig;
namespace NetieSpace.Services;
public sealed class ContentParserService
{
    private static readonly HashSet<string> TextExtensions =
    [
        ".txt", ".text", ".log", ".json", ".xml", ".csv", ".cs", ".js", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".rb", ".php",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".sql", ".sh", ".ps1", ".bat",
        ".html", ".htm", ".css", ".scss", ".vue", ".swift", ".kt", ".md"
    ];
    private static readonly HashSet<string> ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif"
    ];

    private const int MaxCodePreviewChars = 400_000;
    private static readonly HashSet<string> VideoExtensions =
    [
        ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".mpeg", ".mpg",
        ".hevc", ".h265", ".h264", ".3gp", ".flv", ".ts", ".m2ts", ".mts", ".ogv", ".vob"
    ];
    private static readonly HashSet<string> OfficeExtensions =
    [
        ".pdf", ".docx", ".doc", ".odt", ".rtf", ".pptx", ".ppsx", ".ppt", ".pps", ".odp",
        ".xlsx", ".xlsm", ".xls", ".ods"
    ];
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
    private readonly ConversionService _conversion;
    private readonly PreviewConversionCache _previewCache = new();
    private readonly OfficePreviewService _officePreview = new();
    public ContentParserService(ConversionService conversion) => _conversion = conversion;
    public async Task<PreviewContent> ParseAsync(string filePath, int maxImageDimension, CancellationToken ct = default)
    {
        if (IoDirectory.Exists(filePath))
            return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
        var fileInfo = new System.IO.FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var fileName = fileInfo.Name;

        // SVG must not go through WPF BitmapImage — Chromium (WebView2) paints it like a picture.
        if (extension == ".svg")
            return await Task.Run(() => ParseSvg(filePath, fileName, fileInfo.Length), ct);
        // WebP: WPF BitmapImage has no decoder. ParseImage catch was ShellInfo and hid Share.
        if (extension == ".webp")
            return await Task.Run(() => ParseHtmlPicture(filePath, fileName, fileInfo.Length, "image/webp", "WebP"), ct);

        if (ImageExtensions.Contains(extension))
            return await Task.Run(() => ParseImage(filePath, fileName, fileInfo.Length, maxImageDimension), ct);
        if (VideoExtensions.Contains(extension))
        {
            return new PreviewContent
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = PreviewKind.Video,
                FileSizeBytes = fileInfo.Length,
                RawText = $"Video: {fileName}",
                MimeType = GetVideoMime(extension)
            };
        }
        if (extension == ".pdf")
            return await Task.Run(() => ParsePdf(filePath, fileName, fileInfo.Length), ct);
        if (extension is ".md" or ".markdown")
            return await ParseMarkdownAsync(filePath, fileName, fileInfo.Length, ct);
        if (extension is ".html" or ".htm")
            return await ParseHtmlAsync(filePath, fileName, fileInfo.Length, ct);
        if (TextExtensions.Contains(extension))
            return await ParseTextAsync(filePath, fileName, fileInfo.Length, ct);
        if (extension == ".docx")
            return await ParseDocxPreviewAsync(filePath, fileName, fileInfo.Length, fileInfo.LastWriteTimeUtc, ct);
        if (extension is ".pptx" or ".ppsx" or ".pps")
            return await ParsePptxPreviewAsync(filePath, fileName, fileInfo.Length, fileInfo.LastWriteTimeUtc, ct);
        if (extension is ".xlsx" or ".xlsm")
        {
            var native = await Task.Run(() => _officePreview.TryParse(filePath, fileName, fileInfo.Length), ct);
            if (native is not null)
                return native;
            return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
        }
        // Engine already exports these. ShellInfo hid Share Export as (convertOk).
        if (extension is ".odt" or ".rtf" or ".ods" or ".odp")
            return await ParseOfficeHtmlPreviewAsync(filePath, fileName, fileInfo.Length, fileInfo.LastWriteTimeUtc, isPptx: false, ct);
        if (extension is ".doc" or ".xls" or ".exe" or ".dll" or ".msi")
            return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
        if (extension is ".ppt")
            return await ParsePptxPreviewAsync(filePath, fileName, fileInfo.Length, fileInfo.LastWriteTimeUtc, ct);
        if (OfficeExtensions.Contains(extension))
            return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
        return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
    }
    private async Task<PreviewContent> ParseDocxPreviewAsync(
        string filePath, string fileName, long size, DateTime lastWriteUtc, CancellationToken ct)
    {
        return await ParseOfficeHtmlPreviewAsync(filePath, fileName, size, lastWriteUtc, isPptx: false, ct);
    }
    private async Task<PreviewContent> ParsePptxPreviewAsync(
        string filePath, string fileName, long size, DateTime lastWriteUtc, CancellationToken ct)
    {
        return await ParseOfficeHtmlPreviewAsync(filePath, fileName, size, lastWriteUtc, isPptx: true, ct);
    }
    private async Task<PreviewContent> ParseOfficeHtmlPreviewAsync(
        string filePath, string fileName, long size, DateTime lastWriteUtc, bool isPptx, CancellationToken ct)
    {
        var label = isPptx ? "PPTX preview" : "DOCX preview";
        var diskFile = PreviewHtmlDiskCache.TryGetHtmlFile(filePath, size, lastWriteUtc);
        if (diskFile is not null)
        {
            AppLogger.Info($"Office HTML file cache hit for {fileName}");
            var diskHtml = await File.ReadAllTextAsync(diskFile, ct);
            _previewCache.Set(filePath, size, lastWriteUtc, diskHtml, "disk-cache");
            return new PreviewContent
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = PreviewKind.Office,
                FileSizeBytes = size,
                RawText = label,
                RenderedHtml = diskHtml,
                HtmlFilePath = diskFile
            };
        }
        if (isPptx)
        {
            // Real slide bitmaps via PowerPoint COM when installed (true page-by-page)
            var slideHtml = await Task.Run(() => PptxSlideImageService.TryExportSlidesHtml(filePath), ct);
            if (!string.IsNullOrWhiteSpace(slideHtml) && IoFile.Exists(slideHtml))
            {
                var html = await File.ReadAllTextAsync(slideHtml, ct);
                _previewCache.Set(filePath, size, lastWriteUtc, html, "powerpoint-slide-export");
                var saved = PreviewHtmlDiskCache.Write(filePath, size, lastWriteUtc, html);
                return new PreviewContent
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Kind = PreviewKind.Office,
                    FileSizeBytes = size,
                    RawText = label,
                    RenderedHtml = html,
                    HtmlFilePath = saved
                };
            }
            // LibreOffice headless when PowerPoint is missing
            var loHtml = await Task.Run(() => LibreOfficePreviewService.TryExportSlidesHtml(filePath), ct);
            if (!string.IsNullOrWhiteSpace(loHtml) && IoFile.Exists(loHtml))
            {
                var html = await File.ReadAllTextAsync(loHtml, ct);
                _previewCache.Set(filePath, size, lastWriteUtc, html, "libreoffice-pptx");
                // Keep HtmlFilePath next to PNG/PDF siblings under pptx-slides cache
                return new PreviewContent
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Kind = PreviewKind.Office,
                    FileSizeBytes = size,
                    RawText = label,
                    RenderedHtml = html,
                    HtmlFilePath = loHtml
                };
            }
        }
        if (!isPptx && _conversion.IsAvailable)
        {
            var result = await _conversion.PreviewHtmlAsync(filePath, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath) && IoFile.Exists(result.OutputPath))
            {
                // Keep HtmlFilePath next to media/ (mammoth external images) — never NavigateToString
                var bundleHtml = result.OutputPath;
                // Engine already writes under preview-cache/{key}/preview.html + media/
                var html = await File.ReadAllTextAsync(bundleHtml, ct);
                _previewCache.Set(filePath, size, lastWriteUtc, html, result.Tool);
                return new PreviewContent
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Kind = PreviewKind.Office,
                    FileSizeBytes = size,
                    RawText = label,
                    RenderedHtml = html,
                    HtmlFilePath = bundleHtml
                };
            }
            AppLogger.Warn($"mammoth preview-html failed for {fileName}: {result.Error}");
        }
        if (isPptx && _conversion.IsAvailable)
        {
            // pptx2md visual HTML only — MarkItDown is never used for Space visual preview
            var result = await _conversion.PreviewPptxHtmlAsync(filePath, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath) && IoFile.Exists(result.OutputPath))
            {
                var html = await File.ReadAllTextAsync(result.OutputPath, ct);
                _previewCache.Set(filePath, size, lastWriteUtc, html, result.Tool);
                return new PreviewContent
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Kind = PreviewKind.Office,
                    FileSizeBytes = size,
                    RawText = label,
                    RenderedHtml = html,
                    HtmlFilePath = result.OutputPath // keep next to assets/
                };
            }
            AppLogger.Warn($"pptx preview-html failed for {fileName}: {result.Error}");
        }
        var native = await Task.Run(() => _officePreview.TryParse(filePath, fileName, size), ct);
        if (native is not null && !string.IsNullOrWhiteSpace(native.RenderedHtml))
        {
            var saved = PreviewHtmlDiskCache.Write(filePath, size, lastWriteUtc, native.RenderedHtml!);
            _previewCache.Set(filePath, size, lastWriteUtc, native.RenderedHtml!, isPptx ? "openxml-pptx-html" : "openxml-html");
            return new PreviewContent
            {
                FilePath = native.FilePath,
                FileName = native.FileName,
                Kind = native.Kind,
                FileSizeBytes = native.FileSizeBytes,
                RawText = native.RawText,
                RenderedHtml = native.RenderedHtml,
                HtmlFilePath = saved
            };
        }
        return await Task.Run(() => ShellInfoPreview.Build(filePath), ct);
    }
    /// <summary>
    /// Markdown conversion via MarkItDown — used for explicit Convert / to-md only.
    /// Never call this for Space visual preview (Office uses mammoth / COM / LO / OpenXML HTML).
    /// </summary>
    private async Task<PreviewContent> ParseViaConversionAsync(
        string filePath, string fileName, long size, DateTime lastWriteUtc, CancellationToken ct)
    {
        string markdown;
        string? tool;
        if (_previewCache.TryGet(filePath, size, lastWriteUtc, out markdown, out tool))
        {
            AppLogger.Info($"Preview cache hit for {fileName}");
        }
        else
        {
            var result = await _conversion.PreviewConvertAsync(filePath, ct);
            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath) || !IoFile.Exists(result.OutputPath))
            {
                AppLogger.Warn($"Python preview failed for {fileName}: {result.Error}");
                return new PreviewContent
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Kind = PreviewKind.Office,
                    FileSizeBytes = size,
                    RawText = result.Error ?? "Could not convert file for preview."
                };
            }
            markdown = await File.ReadAllTextAsync(result.OutputPath, ct);
            tool = result.Tool;
            _previewCache.Set(filePath, size, lastWriteUtc, markdown, tool);
        }
        var body = Markdown.ToHtml(markdown, _markdownPipeline);
        var html = WrapHtml($"<article class='markdown-body'>{body}</article>");
        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Office,
            FileSizeBytes = size,
            RawText = markdown,
            RenderedHtml = html
        };
    }
    /// <summary>Render SVG as a vector picture in WebView2 (checkerboard + centered art).</summary>
    private static PreviewContent ParseSvg(string filePath, string fileName, long size) =>
        ParseHtmlPicture(filePath, fileName, size, "image/svg+xml", "SVG");

    /// <summary>WebView2 paints the file. Used when WPF BitmapImage has no decoder.</summary>
    private static PreviewContent ParseHtmlPicture(
        string filePath, string fileName, long size, string mime, string label)
    {
        var fileUri = new Uri(filePath).AbsoluteUri;
        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8"/>
              <title>{{Escape(fileName)}}</title>
              <style>
                html, body {
                  margin: 0; height: 100%; overflow: hidden;
                  background-color: #1a2030;
                  background-image:
                    linear-gradient(45deg, #252b3a 25%, transparent 25%),
                    linear-gradient(-45deg, #252b3a 25%, transparent 25%),
                    linear-gradient(45deg, transparent 75%, #252b3a 75%),
                    linear-gradient(-45deg, transparent 75%, #252b3a 75%);
                  background-size: 20px 20px;
                  background-position: 0 0, 0 10px, 10px -10px, -10px 0;
                }
                .stage {
                  display: flex; align-items: center; justify-content: center;
                  width: 100%; height: 100%;
                }
                img {
                  max-width: 92vw; max-height: 92vh; object-fit: contain;
                  filter: drop-shadow(0 12px 40px rgba(0,0,0,0.45));
                }
              </style>
            </head>
            <body>
              <div class="stage">
                <img src="{{fileUri}}" alt="{{Escape(fileName)}}"/>
              </div>
            </body>
            </html>
            """;

        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Image,
            FileSizeBytes = size,
            MimeType = mime,
            RenderedHtml = html,
            RawText = $"{label}: {fileName}"
        };
    }

    private PreviewContent ParseImage(string filePath, string fileName, long size, int maxDimension)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            var mime = GetImageMime(System.IO.Path.GetExtension(filePath));
            var base64 = Convert.ToBase64String(ResizeImageBytes(bytes, maxDimension));
            return new PreviewContent
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = PreviewKind.Image,
                FileSizeBytes = size,
                ImageBase64 = base64,
                MimeType = mime,
                RawText = $"Image: {fileName} ({bitmap.PixelWidth}x{bitmap.PixelHeight})"
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Raster image decode failed for {fileName}: {ex.Message}");
            return ShellInfoPreview.Build(filePath);
        }
    }
    private static byte[] ResizeImageBytes(byte[] bytes, int maxDimension)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= maxDimension && frame.PixelHeight <= maxDimension)
                return bytes;
            var scale = Math.Min((double)maxDimension / frame.PixelWidth, (double)maxDimension / frame.PixelHeight);
            var transformed = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(transformed));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch
        {
            return bytes;
        }
    }
    private PreviewContent ParsePdf(string filePath, string fileName, long size)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        var text = sb.ToString().Trim();
        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Pdf,
            FileSizeBytes = size,
            RawText = text,
            RenderedHtml = null
        };
    }
    private async Task<PreviewContent> ParseMarkdownAsync(string filePath, string fileName, long size, CancellationToken ct)
    {
        var markdown = await File.ReadAllTextAsync(filePath, ct);
        var body = Markdown.ToHtml(markdown, _markdownPipeline);
        var html = WrapHtml($"<article class='markdown-body'>{body}</article>");
        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Markdown,
            FileSizeBytes = size,
            RawText = markdown,
            RenderedHtml = html
        };
    }
    private async Task<PreviewContent> ParseHtmlAsync(string filePath, string fileName, long size, CancellationToken ct)
    {
        var html = await File.ReadAllTextAsync(filePath, ct);
        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Html,
            FileSizeBytes = size,
            RawText = html,
            RenderedHtml = html
        };
    }
    private async Task<PreviewContent> ParseTextAsync(string filePath, string fileName, long size, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(filePath, ct);
        var truncated = false;
        if (text.Length > MaxCodePreviewChars)
        {
            text = text[..MaxCodePreviewChars];
            truncated = true;
        }

        var lang = GetHighlightLanguage(System.IO.Path.GetExtension(filePath));
        var html = WrapCodeHtml(text, lang, fileName, truncated);
        return new PreviewContent
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = PreviewKind.Text,
            FileSizeBytes = size,
            RawText = truncated ? text + "\n\n/* … truncated for preview … */" : text,
            RenderedHtml = html
        };
    }

    private static string GetHighlightLanguage(string extension) => extension.ToLowerInvariant() switch
    {
        ".py" => "python",
        ".cs" => "csharp",
        ".js" or ".jsx" or ".mjs" => "javascript",
        ".ts" or ".tsx" => "typescript",
        ".cpp" or ".cc" or ".cxx" => "cpp",
        ".c" or ".h" => "c",
        ".hpp" or ".hh" => "cpp",
        ".java" => "java",
        ".rs" => "rust",
        ".go" => "go",
        ".rb" => "ruby",
        ".php" => "php",
        ".sql" => "sql",
        ".json" => "json",
        ".xml" => "xml",
        ".yaml" or ".yml" => "yaml",
        ".toml" => "ini",
        ".ini" or ".cfg" => "ini",
        ".css" or ".scss" => "css",
        ".html" or ".htm" or ".vue" => "xml",
        ".sh" => "bash",
        ".ps1" => "powershell",
        ".bat" => "dos",
        ".swift" => "swift",
        ".kt" => "kotlin",
        ".md" or ".markdown" => "markdown",
        ".csv" => "plaintext",
        _ => "plaintext"
    };

    private static string? FindCodeViewerAsset(string fileName)
    {
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "Assets", "code-viewer", fileName),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "src", "NetieSpace", "Assets", "code-viewer", fileName),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "Assets", "code-viewer", fileName),
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }

    private static string WrapCodeHtml(string text, string language, string fileName, bool truncated)
    {
        var cssPath = FindCodeViewerAsset("github-dark.min.css");
        var jsPath = FindCodeViewerAsset("highlight.min.js");
        var cssHref = cssPath is not null ? new Uri(cssPath).AbsoluteUri : "";
        var jsSrc = jsPath is not null ? new Uri(jsPath).AbsoluteUri : "";
        var note = truncated
            ? "<div class='truncate-note'>Preview truncated — open the file in your editor for the full source.</div>"
            : "";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8"/>
              <title>{{Escape(fileName)}}</title>
              {{(string.IsNullOrEmpty(cssHref) ? "" : $"<link rel=\"stylesheet\" href=\"{cssHref}\"/>")}}
              <style>
                html, body {
                  margin: 0; padding: 0;
                  background: #10141c; color: #f2f4f8;
                  font: 13px/1.55 "Cascadia Code", "SF Mono", Consolas, monospace;
                }
                .chrome {
                  position: sticky; top: 0; z-index: 2;
                  display: flex; align-items: center; gap: 10px;
                  padding: 10px 16px;
                  background: rgba(16,20,28,0.92);
                  border-bottom: 1px solid rgba(255,255,255,0.08);
                  backdrop-filter: blur(10px);
                  font: 11px/1 "Segoe UI", system-ui, sans-serif;
                  letter-spacing: 0.04em;
                  text-transform: uppercase;
                  color: #99a3b8;
                }
                .chip {
                  background: rgba(94,235,216,0.12);
                  color: #5eebd8;
                  border: 1px solid rgba(94,235,216,0.28);
                  border-radius: 6px;
                  padding: 3px 8px;
                  font-weight: 600;
                }
                .name { color: #f2f4f8; text-transform: none; letter-spacing: 0; font-size: 12px; }
                .truncate-note {
                  margin: 12px 16px 0;
                  padding: 8px 12px;
                  border-radius: 8px;
                  background: rgba(139,157,255,0.12);
                  color: #c5ceff;
                  font: 12px/1.4 "Segoe UI", system-ui, sans-serif;
                }
                pre {
                  margin: 0; padding: 18px 16px 40px;
                  background: transparent !important;
                  white-space: pre-wrap;
                  overflow-wrap: anywhere;
                }
                code.hljs { background: transparent !important; padding: 0; }
                {{GlassScrollbarCss.StyleBlock}}
              </style>
            </head>
            <body>
              <div class="chrome">
                <span class="chip">{{Escape(language)}}</span>
                <span class="name">{{Escape(fileName)}}</span>
              </div>
              {{note}}
              <pre><code class="language-{{Escape(language)}}">{{Escape(text)}}</code></pre>
              {{(string.IsNullOrEmpty(jsSrc) ? "" : $"<script src=\"{jsSrc}\"></script><script>try{{hljs.highlightAll();}}catch(e){{}}</script>")}}
            </body>
            </html>
            """;
    }
    private static string WrapHtml(string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8"/>
          <style>
            :root {
              color-scheme: dark;
              --bg: #10141c;
              --fg: #f2f4f8;
              --muted: #99a3b8;
              --accent: #8b9dff;
              --accent2: #5eebd8;
              --border: rgba(255,255,255,0.12);
              --glass: rgba(255,255,255,0.06);
            }
            * { box-sizing: border-box; }
            {{GlassScrollbarCss.StyleBlock}}
            html, body {
              margin: 0;
              padding: 32px 36px 52px;
              background: var(--bg);
              color: var(--fg);
              font: 15px/1.75 "Segoe UI Variable Text", "SF Pro Text", "Helvetica Neue", "Segoe UI", system-ui, sans-serif;
              -webkit-font-smoothing: antialiased;
              overflow-wrap: anywhere;
            }
            ::selection { background: rgba(139, 157, 255, 0.38); }
            h1,h2,h3 {
              font-family: "Segoe UI Variable Display", "SF Pro Display", "Helvetica Neue", sans-serif;
              color: #fff; margin-top: 1.5em; letter-spacing: -0.025em; font-weight: 600;
            }
            h1 { font-size: 1.9em; }
            a { color: var(--accent); text-decoration: none; }
            blockquote {
              border-left: 2px solid var(--accent);
              margin: 1.2em 0;
              padding: 10px 18px;
              background: var(--glass);
              border-radius: 0 14px 14px 0;
              color: var(--muted);
              backdrop-filter: blur(12px);
            }
            pre, code {
              font-family: "Cascadia Code", "SF Mono", Consolas, monospace;
              font-size: 13px;
            }
            pre {
              background: rgba(0,0,0,0.35);
              border: 1px solid var(--border);
              border-radius: 14px;
              padding: 20px;
              overflow-x: auto;
              white-space: pre-wrap;
            }
            .convert-badge {
              display: inline-block;
              background: linear-gradient(135deg, rgba(100,120,255,0.5), rgba(60,200,180,0.4));
              border: 1px solid rgba(255,255,255,0.2);
              color: #e8ecff;
              font-size: 10.5px;
              font-weight: 600;
              letter-spacing: 0.06em;
              text-transform: uppercase;
              padding: 6px 14px;
              border-radius: 20px;
              margin-bottom: 22px;
              backdrop-filter: blur(8px);
            }
            .markdown-body img { max-width: 100%; border-radius: 10px; box-shadow: 0 8px 32px rgba(0,0,0,0.4); }
            .markdown-body table {
              border-collapse: collapse;
              width: 100%;
              margin: 1.2em 0;
              background: var(--card);
              border-radius: 10px;
              overflow: hidden;
            }
            .markdown-body th, .markdown-body td {
              border: 1px solid var(--border);
              padding: 10px 14px;
            }
            .markdown-body th { background: #1a2030; color: var(--accent2); }
            hr { border: none; border-top: 1px solid var(--border); margin: 2em 0; }
            .md-toc {
              position: sticky; top: 0; z-index: 5; margin: -8px 0 18px;
              padding: 10px 12px; border-radius: 12px;
              background: rgba(16,20,28,0.88); border: 1px solid rgba(255,255,255,0.1);
              backdrop-filter: blur(12px); display: none; gap: 8px; flex-wrap: wrap;
            }
            .md-toc a {
              color: var(--accent2); font-size: 12px; text-decoration: none;
              padding: 4px 8px; border-radius: 8px; background: rgba(255,255,255,0.04);
            }
            .md-toc a:hover { background: rgba(94,196,184,0.18); }
          </style>
        </head>
        <body>{{body}}
        <script>
        (function(){
          var article=document.querySelector('.markdown-body');
          if(!article) return;
          var heads=[].slice.call(article.querySelectorAll('h1,h2,h3')).slice(0,16);
          if(heads.length<2) return;
          var toc=document.createElement('nav');
          toc.className='md-toc';
          toc.style.display='flex';
          heads.forEach(function(h,i){
            if(!h.id) h.id='h'+i;
            var a=document.createElement('a');
            a.href='#'+h.id;
            a.textContent=h.textContent||('§'+(i+1));
            toc.appendChild(a);
          });
          article.insertBefore(toc, article.firstChild);
        })();
        </script>
        </body>
        </html>
        """;
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string GetImageMime(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".tif" or ".tiff" => "image/tiff",
        ".ico" => "image/x-icon",
        ".heic" or ".heif" => "image/heic",
        _ => "image/jpeg"
    };
    private static string GetVideoMime(string extension) => extension.ToLowerInvariant() switch
    {
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".mov" => "video/quicktime",
        ".wmv" => "video/x-ms-wmv",
        _ => "video/mp4"
    };
}

