using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using NetieSpace.Helpers;
using NetieSpace.Models;
using A = DocumentFormat.OpenXml.Drawing;
using M = DocumentFormat.OpenXml.Math;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace NetieSpace.Services;

/// <summary>
/// Pure C# Office preview — HTML with images for DOCX; markdown fallbacks for xlsx/pptx.
/// </summary>
public sealed class OfficePreviewService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public PreviewContent? TryParse(string filePath, string fileName, long size)
    {
        var ext = IoPath.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".docx" => FromHtml(filePath, fileName, size, DocxToHtml(filePath), "openxml-html"),
                ".xlsx" or ".xlsm" => FromXlsx(filePath, fileName, size),
                ".pptx" or ".ppsx" or ".pps" => FromHtml(filePath, fileName, size, PptxToHtml(filePath), "openxml-pptx-html"),
                _ => null
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"OpenXML preview failed for {fileName}: {ex.Message}");
            return null;
        }
    }

    private PreviewContent FromXlsx(string path, string name, long size)
    {
        var htmlBody = XlsxToHtml(path);
        var html = WrapHtml($"<article class='office-body'>{htmlBody}</article>");
        return new PreviewContent
        {
            FilePath = path,
            FileName = name,
            Kind = PreviewKind.Office,
            FileSizeBytes = size,
            RawText = XlsxDigest(path),
            RenderedHtml = html
        };
    }

    private PreviewContent FromHtml(string path, string name, long size, string htmlBody, string tool)
    {
        var html = WrapHtml($"<article class='office-body'>{htmlBody}</article>");
        return new PreviewContent
        {
            FilePath = path,
            FileName = name,
            Kind = PreviewKind.Office,
            FileSizeBytes = size,
            RawText = StripTags(htmlBody),
            RenderedHtml = html
        };
    }

    private PreviewContent FromMarkdown(string path, string name, long size, string markdown, string tool)
    {
        var body = Markdown.ToHtml(markdown, _pipeline);
        var html = WrapHtml($"<article class='markdown-body'>{body}</article>");
        return new PreviewContent
        {
            FilePath = path,
            FileName = name,
            Kind = PreviewKind.Office,
            FileSizeBytes = size,
            RawText = markdown,
            RenderedHtml = html
        };
    }

    /// <summary>Lightweight DOCX → HTML with embedded media (benchmark openxml_html).</summary>
    public static string DocxToHtml(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body;
        if (main is null || body is null)
            return "<p><em>Empty document</em></p>";

        var sb = new StringBuilder();
        foreach (var para in body.Elements<Paragraph>())
        {
            sb.Append("<p>");
            AppendParagraphContent(sb, para, main);
            sb.Append("</p>");
        }

        return sb.ToString();
    }

    /// <summary>Extract readable LaTeX-ish text from OMML for KaTeX rendering.</summary>
    private static string OmmlToLatex(M.OfficeMath math)
    {
        var parts = math.Descendants<M.Text>()
            .Select(t => t.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));
        var combined = string.Join(" ", parts);
        return combined.Trim();
    }

    private static void AppendParagraphContent(StringBuilder sb, Paragraph para, MainDocumentPart main)
    {
        foreach (var node in para.Descendants())
        {
            switch (node)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Text t when t.Text is { Length: > 0 }:
                    sb.Append(System.Net.WebUtility.HtmlEncode(t.Text));
                    break;
                case PIC.Picture pic:
                    TryAppendImage(sb, pic.BlipFill?.Blip?.Embed?.Value, main);
                    break;
                case A.Blip blip:
                    TryAppendImage(sb, blip.Embed?.Value, main);
                    break;
                case DocumentFormat.OpenXml.Math.OfficeMath om:
                    var latex = OmmlToLatex(om);
                    if (!string.IsNullOrWhiteSpace(latex))
                        sb.Append($"<span class=\"math-tex\">\\({System.Net.WebUtility.HtmlEncode(latex)}\\)</span>");
                    else
                        sb.Append("<em>[formula]</em>");
                    break;
            }
        }
    }

    private static void TryAppendImage(StringBuilder sb, string? relId, MainDocumentPart main)
    {
        if (string.IsNullOrWhiteSpace(relId))
            return;

        try
        {
            if (main.GetPartById(relId) is not ImagePart imagePart)
                return;

            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return;

            var contentType = imagePart.ContentType ?? "image/png";
            var b64 = Convert.ToBase64String(bytes);
            sb.Append($"<img src=\"data:{contentType};base64,{b64}\" alt=\"image\"/>");
        }
        catch
        {
            // skip broken images
        }
    }

    private static string StripTags(string html) =>
        Regex.Replace(html, "<[^>]+>", " ").Trim();

    /// <summary>Richer XLSX grid as HTML table (sticky header, more rows/cols).</summary>
    public static string XlsxToHtml(string path, int maxRows = 200, int maxCols = 40)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbook = doc.WorkbookPart;
        if (workbook is null)
            return "<p><em>Empty workbook</em></p>";

        var sb = new StringBuilder();
        sb.Append($"<h1>{System.Net.WebUtility.HtmlEncode(IoPath.GetFileNameWithoutExtension(path))}</h1>");

        var shared = workbook.SharedStringTablePart?.SharedStringTable;
        var sheetIndex = 0;
        foreach (var sheet in workbook.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            sheetIndex++;
            var sheetName = sheet.Name?.Value ?? $"Sheet {sheetIndex}";
            var wrapDetails = sheetIndex > 1;
            var encName = System.Net.WebUtility.HtmlEncode(sheetName);
            if (wrapDetails)
                sb.Append($"<details class='xlsx-sheet' data-sheet='{encName}'><summary>{encName}</summary>");
            else
                sb.Append($"<section class='xlsx-sheet' data-sheet='{encName}' open><h2>{encName}</h2>");

            if (sheet.Id?.Value is null)
            {
                sb.Append("<p><em>Missing sheet data</em></p>");
                if (wrapDetails) sb.Append("</details>");
                else sb.Append("</section>");
                continue;
            }

            var part = (WorksheetPart)workbook.GetPartById(sheet.Id.Value);
            var rows = part.Worksheet.Descendants<Row>().Take(maxRows).ToList();
            if (rows.Count == 0)
            {
                sb.Append("<p><em>Empty sheet</em></p>");
                if (wrapDetails) sb.Append("</details>");
                else sb.Append("</section>");
                continue;
            }

            var matrix = rows.Select(r => ExpandRow(r, shared, maxCols)).ToList();
            var width = Math.Min(maxCols, Math.Max(1, matrix.Max(r => r.Count)));
            foreach (var row in matrix)
                while (row.Count < width)
                    row.Add("");

            sb.Append("<div class='xlsx-wrap'><table class='xlsx-grid'><thead><tr>");
            for (var c = 0; c < width; c++)
                sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(matrix[0][c])}</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in matrix.Skip(1))
            {
                sb.Append("<tr>");
                for (var c = 0; c < width; c++)
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row[c])}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table></div>");

            var totalRows = part.Worksheet.Descendants<Row>().Count();
            if (totalRows > maxRows || WidthHint(part, maxCols) > maxCols)
                sb.Append($"<p class='xlsx-note'>Showing first {Math.Min(maxRows, totalRows)} rows × {width} cols</p>");

            if (wrapDetails)
                sb.Append("</details>");
            else
                sb.Append("</section>");
        }

        return sb.ToString();
    }

    /// <summary>Compact digest for AI / local summary — not a raw cell dump.</summary>
    public static string XlsxDigest(string path, int headerRows = 3, int sampleRows = 8, int maxCols = 12)
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(path, false);
            var workbook = doc.WorkbookPart;
            if (workbook is null)
                return IoPath.GetFileName(path);

            var shared = workbook.SharedStringTablePart?.SharedStringTable;
            var sb = new StringBuilder();
            sb.AppendLine($"Workbook: {IoPath.GetFileNameWithoutExtension(path)}");
            var sheets = workbook.Workbook.Sheets?.Elements<Sheet>()?.ToList() ?? [];
            sb.AppendLine($"Sheets ({sheets.Count}): {string.Join(", ", sheets.Select(s => s.Name?.Value ?? "?"))}");
            sb.AppendLine();

            var index = 0;
            foreach (var sheet in sheets.Take(8))
            {
                index++;
                if (sheet.Id?.Value is null) continue;
                AppendSheetDigest(sb, sheet.Name?.Value ?? $"Sheet {index}",
                    (WorksheetPart)workbook.GetPartById(sheet.Id.Value), shared, headerRows, sampleRows, maxCols);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"{IoPath.GetFileName(path)}\n(Excel digest failed: {ex.Message})";
        }
    }

    /// <summary>Single-sheet digest for Look Up when an active sheet is known.</summary>
    public static string XlsxSheetDigest(string path, string sheetName, int headerRows = 4, int sampleRows = 40, int maxCols = 16)
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(path, false);
            var workbook = doc.WorkbookPart;
            if (workbook is null)
                return sheetName;

            var shared = workbook.SharedStringTablePart?.SharedStringTable;
            var sheets = workbook.Workbook.Sheets?.Elements<Sheet>()?.ToList() ?? [];
            var sheet = sheets.FirstOrDefault(s =>
                string.Equals(s.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet?.Id?.Value is null)
                return XlsxDigest(path);

            var sb = new StringBuilder();
            sb.AppendLine($"Workbook: {IoPath.GetFileNameWithoutExtension(path)}");
            sb.AppendLine($"Active sheet: {sheet.Name?.Value}");
            sb.AppendLine();
            AppendSheetDigest(sb, sheet.Name!.Value!,
                (WorksheetPart)workbook.GetPartById(sheet.Id.Value), shared, headerRows, sampleRows, maxCols);
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"{sheetName}\n(Excel sheet digest failed: {ex.Message})";
        }
    }

    private static void AppendSheetDigest(
        StringBuilder sb,
        string sheetName,
        WorksheetPart part,
        SharedStringTable? shared,
        int headerRows,
        int sampleRows,
        int maxCols)
    {
        var rows = part.Worksheet.Descendants<Row>().Take(headerRows + sampleRows).ToList();
        sb.AppendLine($"## {sheetName}");
        if (rows.Count == 0)
        {
            sb.AppendLine("(empty)");
            sb.AppendLine();
            return;
        }

        var matrix = rows.Select(r => ExpandRow(r, shared, maxCols)).ToList();
        var width = Math.Min(maxCols, Math.Max(1, matrix.Max(r => r.Count)));
        foreach (var row in matrix)
            while (row.Count < width) row.Add("");

        // Markdown table with column letters for positional fidelity
        var cols = Enumerable.Range(0, width).Select(ColLetter).ToList();
        sb.Append("| row | ").Append(string.Join(" | ", cols)).AppendLine(" |");
        sb.Append("| --- | ").Append(string.Join(" | ", cols.Select(_ => "---"))).AppendLine(" |");

        for (var r = 0; r < matrix.Count; r++)
        {
            var rowNum = (int)(rows[r].RowIndex?.Value ?? (uint)(r + 1));
            var cells = matrix[r].Take(width).Select(c => EscapeMdCell(c));
            sb.Append("| ").Append(rowNum).Append(" | ").Append(string.Join(" | ", cells)).AppendLine(" |");
        }

        sb.AppendLine($"<!-- refs like {sheetName}!A1 -->");
        sb.AppendLine();
    }

    private static string ColLetter(int index)
    {
        var n = index;
        var s = "";
        do
        {
            s = (char)('A' + n % 26) + s;
            n = n / 26 - 1;
        } while (n >= 0);
        return s;
    }

    private static string EscapeMdCell(string c) =>
        (c ?? "").Replace("|", "\\|").Replace("\n", " ").Trim();

    private static int WidthHint(WorksheetPart part, int maxCols)
    {
        var max = 0;
        foreach (var cell in part.Worksheet.Descendants<Cell>().Take(5000))
        {
            var col = ColumnIndex(cell.CellReference?.Value);
            if (col > max) max = col;
            if (max >= maxCols) return maxCols + 1;
        }
        return max;
    }

    private static List<string> ExpandRow(Row row, SharedStringTable? shared, int maxCols)
    {
        var cells = new string[maxCols];
        Array.Fill(cells, "");
        foreach (var cell in row.Elements<Cell>())
        {
            var col = ColumnIndex(cell.CellReference?.Value) - 1;
            if (col < 0 || col >= maxCols)
                continue;
            cells[col] = GetCellText(cell, shared).Replace("|", "/").Trim();
        }

        return cells.ToList();
    }

    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrWhiteSpace(cellRef))
            return 0;
        var letters = Regex.Match(cellRef, "^[A-Za-z]+").Value.ToUpperInvariant();
        var n = 0;
        foreach (var c in letters)
            n = n * 26 + (c - 'A' + 1);
        return n;
    }

    private static string GetCellText(Cell cell, SharedStringTable? shared)
    {
        var value = cell.CellValue?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var idx) &&
            shared is not null &&
            idx < shared.ChildElements.Count)
        {
            return shared.ElementAt(idx).InnerText;
        }

        return value;
    }

    private static string PptxToHtml(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var presentation = doc.PresentationPart;
        if (presentation?.Presentation?.SlideIdList is null)
            return "<p><em>Empty presentation</em></p>";

        var sb = new StringBuilder();
        var i = 1;
        foreach (var slideId in presentation.Presentation.SlideIdList.Elements<SlideId>())
        {
            if (slideId.RelationshipId?.Value is null)
                continue;

            var part = (SlidePart)presentation.GetPartById(slideId.RelationshipId.Value);
            sb.Append($"<section class='slide'><h2>Slide {i++}</h2><div class='slide-body'>");

            foreach (var imagePart in part.ImageParts)
            {
                try
                {
                    using var stream = imagePart.GetStream();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    if (bytes.Length == 0)
                        continue;
                    var ct = imagePart.ContentType ?? "image/png";
                    sb.Append($"<img src=\"data:{ct};base64,{Convert.ToBase64String(bytes)}\" alt=\"slide image\"/>");
                }
                catch
                {
                    // skip
                }
            }

            var texts = part.Slide.Descendants<A.Text>()
                .Select(t => t.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();
            foreach (var t in texts)
                sb.Append($"<p>{System.Net.WebUtility.HtmlEncode(t)}</p>");

            sb.Append("</div></section>");
        }

        return sb.ToString();
    }

    private static string PptxToMarkdown(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var sb = new StringBuilder();
        sb.AppendLine($"# {IoPath.GetFileNameWithoutExtension(path)}");
        sb.AppendLine();

        var presentation = doc.PresentationPart;
        if (presentation?.Presentation?.SlideIdList is null)
            return "_Empty presentation_";

        var i = 1;
        foreach (var slideId in presentation.Presentation.SlideIdList.Elements<SlideId>())
        {
            if (slideId.RelationshipId?.Value is null)
                continue;

            var part = (SlidePart)presentation.GetPartById(slideId.RelationshipId.Value);
            var texts = part.Slide.Descendants<A.Text>()
                .Select(t => t.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            sb.AppendLine($"## Slide {i++}");
            sb.AppendLine();
            foreach (var t in texts)
                sb.AppendLine($"- {t}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string WrapHtml(string body) => $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"/>
        <meta name="viewport" content="width=device-width, initial-scale=1"/>
        <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css"/>
        <style>
          {{GlassScrollbarCss.StyleBlock}}
          html,body{margin:0;height:100%;background:#10141c;color:#f2f4f8;
            font:15px/1.7 "Segoe UI Variable Text","SF Pro Text",sans-serif}
          body{overflow:auto;padding:0}
          article.office-body{padding:28px 32px;max-width:920px;margin:0 auto;
            box-sizing:border-box}
          h1,h2,h3{color:#fff;letter-spacing:-.02em}
          img{max-width:100%;height:auto;margin:12px 0;border-radius:4px}
          .katex-display{margin:1em 0;overflow-x:auto}
          #deck,body:has(> .slide){height:100%;overflow-y:auto;scroll-snap-type:y mandatory;
            scroll-behavior:smooth;padding:12px 16px 24px;box-sizing:border-box}
          .slide{scroll-snap-align:start;min-height:calc(100% - 12px);margin:0 0 20px;
            padding:22px 24px;border-radius:16px;box-sizing:border-box;
            background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.12);
            display:flex;flex-direction:column}
          .slide h2{margin:0 0 12px}
          .slide-body{flex:1}
          .slide-body img{max-width:100%;max-height:70vh;object-fit:contain;display:block;
            margin:8px auto;border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,.35)}
          table{border-collapse:collapse;width:100%;margin:1em 0}
          th,td{border:1px solid rgba(255,255,255,.12);padding:8px 10px}
          th{background:#1a2030}
          .xlsx-wrap{overflow:auto;max-height:calc(100vh - 120px);border:1px solid rgba(255,255,255,.1);
            border-radius:10px;margin:12px 0}
          table.xlsx-grid{border-collapse:separate;border-spacing:0;width:max-content;min-width:100%;margin:0;
            font:12.5px/1.35 "Cascadia Code","Consolas",monospace}
          .xlsx-grid th,.xlsx-grid td{border-bottom:1px solid rgba(255,255,255,.1);
            border-right:1px solid rgba(255,255,255,.08);padding:6px 10px;white-space:nowrap;max-width:280px;
            overflow:hidden;text-overflow:ellipsis}
          .xlsx-grid thead th{position:sticky;top:0;z-index:2;background:#1a2030;color:#9fd4c8;
            box-shadow:0 1px 0 rgba(255,255,255,.12)}
          .xlsx-grid tbody tr:nth-child(even){background:rgba(255,255,255,.03)}
          .xlsx-note{color:#8a93a6;font-size:12px;margin-top:8px}
          details.xlsx-sheet{margin:16px 0;padding:10px 12px;border-radius:10px;
            background:rgba(255,255,255,.03);border:1px solid rgba(255,255,255,.08)}
          details.xlsx-sheet summary{cursor:pointer;color:#9fd4c8;font-weight:600;margin-bottom:8px}
          @media (max-width:700px){
            article.office-body{padding:16px}
            body{font-size:14px}
          }
        </style>
        <script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js"></script>
        <script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/contrib/auto-render.min.js"></script>
        <script>
        function netieRenderMath(){
          if(typeof renderMathInElement!=='function')return;
          try{
            renderMathInElement(document.body,{
              delimiters:[
                {left:'$$',right:'$$',display:true},
                {left:'$',right:'$',display:false},
                {left:'\\\\(',right:'\\\\)',display:false},
                {left:'\\\\[',right:'\\\\]',display:true}
              ],
              throwOnError:false
            });
            document.querySelectorAll('.math-tex').forEach(function(el){
              try{
                var tex=el.textContent||'';
                katex.render(tex.replace(/^\\\\(|\\\\)$/g,'').replace(/^\\\\[|\\\\]$/g,''),el,{displayMode:tex.indexOf('\\\\[')===0,throwOnError:false});
              }catch(e){}
            });
          }catch(e){}
        }
        document.addEventListener('DOMContentLoaded',netieRenderMath);
        window.addEventListener('load',netieRenderMath);
        function netiePostActiveSheet(name){
          try{
            if(window.chrome&&chrome.webview)
              chrome.webview.postMessage(JSON.stringify({type:'activeSheet',name:name||''}));
          }catch(e){}
        }
        function netieWireSheets(){
          var sheets=document.querySelectorAll('.xlsx-sheet[data-sheet]');
          if(!sheets.length)return;
          var first=sheets[0].getAttribute('data-sheet');
          if(first) netiePostActiveSheet(first);
          sheets.forEach(function(el){
            el.addEventListener('toggle',function(){
              if(el.open) netiePostActiveSheet(el.getAttribute('data-sheet'));
            });
            el.addEventListener('click',function(){
              netiePostActiveSheet(el.getAttribute('data-sheet'));
            });
          });
        }
        document.addEventListener('DOMContentLoaded',netieWireSheets);
        window.addEventListener('load',netieWireSheets);
        </script>
        </head><body>{{body}}</body></html>
        """;

    /// <summary>DOCX or HTML preview that can round-trip through the WebView contenteditable path.</summary>
    public static bool IsHtmlWordEditable(PreviewKind kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        if (kind == PreviewKind.Html && ext is ".html" or ".htm")
            return true;
        return kind == PreviewKind.Office && ext == ".docx";
    }

    /// <summary>
    /// Persist an edited WebView document. HTML files are written as HTML.
    /// Existing local .docx files are rewritten via OpenXML (no extra NuGet / no iText).
    /// </summary>
    public static HtmlSaveResult SaveEditedPreview(
        string destinationPath,
        string documentHtml,
        string? articleHtml = null,
        string? mediaBaseDir = null)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return new HtmlSaveResult(false, "No file path.");

        var ext = IoPath.GetExtension(destinationPath).ToLowerInvariant();
        try
        {
            if (ext is ".html" or ".htm")
            {
                var html = StripEditorChrome(string.IsNullOrWhiteSpace(documentHtml) ? articleHtml : documentHtml);
                AtomicWriteText(destinationPath, EnsureHtmlDocument(html ?? ""));
                return new HtmlSaveResult(true, "Saved HTML");
            }

            if (ext == ".docx")
            {
                if (!IoFile.Exists(destinationPath))
                    return new HtmlSaveResult(false, "Word save needs an existing local .docx path.");

                var bodyHtml = string.IsNullOrWhiteSpace(articleHtml) ? documentHtml : articleHtml;
                var tmp = IoPath.Combine(IoPath.GetTempPath(), $"netie-html-{Guid.NewGuid():N}.docx");
                try
                {
                    HtmlDocxWriter.Write(tmp, bodyHtml ?? "", mediaBaseDir);
                    CopyReplace(tmp, destinationPath);
                }
                finally
                {
                    try { IoFile.Delete(tmp); } catch { /* ignore */ }
                }

                return new HtmlSaveResult(true, "Saved Word document");
            }

            return new HtmlSaveResult(false, $"Cannot save edits as {ext}.");
        }
        catch (Exception ex)
        {
            return new HtmlSaveResult(false, ex.Message);
        }
    }

    public static string EnsureHtmlDocument(string html)
    {
        var t = (html ?? "").Trim();
        if (t.Length == 0)
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body></body></html>";
        if (t.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return t;
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body>" + t + "</body></html>";
    }

    public static string StripEditorChrome(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return html ?? "";
        var s = Regex.Replace(html, @"\scontenteditable\s*=\s*(['""])(true|false)\1", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\scontenteditable(?=[\s>])", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"outline:\s*1px solid rgba\(94,\s*196,\s*184,\s*0\.45\);?", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"outline:\s*2px solid #5EC4B8;?", "", RegexOptions.IgnoreCase);
        return s;
    }

    private static void AtomicWriteText(string path, string contents)
    {
        var tmp = path + ".netie-tmp";
        IoFile.WriteAllText(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CopyReplace(tmp, path);
        try { IoFile.Delete(tmp); } catch { /* ignore */ }
    }

    private static void CopyReplace(string source, string destination)
    {
        IOException? last = null;
        for (var i = 0; i < 4; i++)
        {
            try
            {
                IoFile.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(80 * (i + 1));
            }
        }

        throw last ?? new IOException("Could not replace " + destination);
    }
}

public readonly record struct HtmlSaveResult(bool Success, string Message);
