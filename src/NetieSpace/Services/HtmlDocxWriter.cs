using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace NetieSpace.Services;

/// <summary>Best-effort HTML → OpenXML (paragraphs, headings, lists, tables, local/data images).</summary>
internal static class HtmlDocxWriter
{
    public static void Write(string outputPath, string html, string? mediaBaseDir)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        var ctx = new WriteContext(main, mediaBaseDir);

        foreach (var block in ParseBlocks(html ?? "", ctx))
            body.AppendChild(block);

        if (!body.Elements<Paragraph>().Any() && !body.Elements<Table>().Any())
            body.AppendChild(new Paragraph(new Run(new Text(""))));

        body.AppendChild(new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }));
        main.Document.Save();
    }

    private sealed class WriteContext(MainDocumentPart main, string? mediaBaseDir)
    {
        public MainDocumentPart Main { get; } = main;
        public string? MediaBaseDir { get; } = mediaBaseDir;
        public uint DrawingId { get; set; } = 1;
        public InlineFormat Format;
    }

    private struct InlineFormat
    {
        public bool Bold, Italic, Underline, Code;
        public int HeadingLevel;
    }

    private static IEnumerable<OpenXmlElement> ParseBlocks(string html, WriteContext ctx)
    {
        html = StripChrome(html);
        var tokens = Tokenize(html);
        var blocks = new List<OpenXmlElement>();
        var runs = new List<Run>();
        var heading = 0;
        var skipDepth = 0;
        var skipTag = "";
        var inTable = false;
        List<List<List<Run>>>? table = null;
        List<List<Run>>? tableRow = null;
        List<Run>? tableCell = null;
        var listDepth = 0;

        void FlushParagraph()
        {
            if (inTable)
                return;
            if (runs.Count == 0)
                return;
            blocks.Add(MakeParagraph(runs, heading, listDepth));
            runs = new List<Run>();
            heading = 0;
        }

        void FlushCell()
        {
            if (tableCell is null || tableRow is null)
                return;
            if (tableCell.Count == 0)
                tableCell.Add(MakeTextRun("", ctx.Format));
            tableRow.Add(tableCell);
            tableCell = null;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var (isTag, raw) = tokens[i];
            if (!isTag)
            {
                if (skipDepth > 0)
                    continue;
                var text = WebUtility.HtmlDecode(raw);
                if (string.IsNullOrEmpty(text))
                    continue;
                if (string.IsNullOrWhiteSpace(text) && runs.Count == 0 && tableCell is null)
                    continue;
                var run = MakeTextRun(text, ctx.Format);
                if (tableCell is not null)
                    tableCell.Add(run);
                else
                    runs.Add(run);
                continue;
            }

            var (close, selfClosing, name, attrs) = ParseTag(raw);
            if (name.Length == 0)
                continue;

            if (skipDepth > 0)
            {
                if (!close && name == skipTag && !selfClosing)
                    skipDepth++;
                else if (close && name == skipTag)
                {
                    skipDepth--;
                    if (skipDepth == 0)
                        skipTag = "";
                }
                continue;
            }

            if (!close && name is "script" or "style" or "head" or "noscript" or "svg")
            {
                if (!selfClosing)
                {
                    skipDepth = 1;
                    skipTag = name;
                }
                continue;
            }

            switch (name)
            {
                case "br":
                case "hr":
                    if (tableCell is not null)
                        tableCell.Add(new Run(new Break()));
                    else
                        FlushParagraph();
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    if (close)
                    {
                        FlushParagraph();
                        ctx.Format.HeadingLevel = 0;
                    }
                    else
                    {
                        FlushParagraph();
                        heading = name[1] - '0';
                        ctx.Format.HeadingLevel = heading;
                    }
                    break;
                case "p":
                case "div":
                case "blockquote":
                case "section":
                case "article":
                case "pre":
                    if (close)
                        FlushParagraph();
                    else if (runs.Count > 0)
                        FlushParagraph();
                    if (name == "pre")
                        ctx.Format.Code = !close;
                    break;
                case "ul":
                case "ol":
                    if (close)
                    {
                        FlushParagraph();
                        listDepth = Math.Max(0, listDepth - 1);
                    }
                    else
                    {
                        FlushParagraph();
                        listDepth++;
                    }
                    break;
                case "li":
                    if (close)
                        FlushParagraph();
                    else if (runs.Count > 0)
                        FlushParagraph();
                    break;
                case "table":
                    if (close)
                    {
                        FlushCell();
                        if (tableRow is not null && table is not null)
                        {
                            if (tableRow.Count > 0)
                                table.Add(tableRow);
                            tableRow = null;
                        }
                        if (table is { Count: > 0 })
                            blocks.Add(MakeTable(table));
                        table = null;
                        inTable = false;
                    }
                    else
                    {
                        FlushParagraph();
                        inTable = true;
                        table = [];
                        tableRow = null;
                        tableCell = null;
                    }
                    break;
                case "tr":
                    if (!inTable || table is null)
                        break;
                    if (close)
                    {
                        FlushCell();
                        if (tableRow is { Count: > 0 })
                            table.Add(tableRow);
                        tableRow = null;
                    }
                    else
                    {
                        FlushCell();
                        if (tableRow is { Count: > 0 })
                            table.Add(tableRow);
                        tableRow = [];
                        tableCell = null;
                    }
                    break;
                case "td":
                case "th":
                    if (!inTable)
                        break;
                    tableRow ??= [];
                    if (close)
                        FlushCell();
                    else
                    {
                        FlushCell();
                        tableCell = [];
                        if (name == "th")
                            ctx.Format.Bold = true;
                    }
                    if (close && name == "th")
                        ctx.Format.Bold = false;
                    break;
                case "b":
                case "strong":
                    ctx.Format.Bold = !close;
                    break;
                case "i":
                case "em":
                    ctx.Format.Italic = !close;
                    break;
                case "u":
                    ctx.Format.Underline = !close;
                    break;
                case "code":
                case "tt":
                    ctx.Format.Code = !close;
                    break;
                case "img":
                    if (close)
                        break;
                    var drawing = TryMakeImageRun(attrs, ctx);
                    if (drawing is not null)
                    {
                        if (tableCell is not null)
                            tableCell.Add(drawing);
                        else
                            runs.Add(drawing);
                    }
                    break;
            }
        }

        FlushParagraph();
        if (inTable && table is { Count: > 0 })
            blocks.Add(MakeTable(table));

        return blocks;
    }

    private static string StripChrome(string html)
    {
        var s = html;
        var article = Regex.Match(s, @"<article\b[^>]*class=['""][^'""]*office-body[^'""]*['""][^>]*>(.*)</article>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (article.Success)
            s = article.Groups[1].Value;
        else
        {
            var body = Regex.Match(s, @"<body\b[^>]*>(.*)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (body.Success)
                s = body.Groups[1].Value;
        }

        return OfficePreviewService.StripEditorChrome(s);
    }

    private static List<(bool isTag, string raw)> Tokenize(string html)
    {
        var list = new List<(bool, string)>();
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                if (i + 3 < html.Length && html.AsSpan(i).StartsWith("<!--"))
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? html.Length : end + 3;
                    continue;
                }

                var gt = html.IndexOf('>', i + 1);
                if (gt < 0)
                {
                    list.Add((false, html[i..]));
                    break;
                }

                list.Add((true, html[(i + 1)..gt]));
                i = gt + 1;
            }
            else
            {
                var lt = html.IndexOf('<', i);
                if (lt < 0)
                {
                    list.Add((false, html[i..]));
                    break;
                }

                list.Add((false, html[i..lt]));
                i = lt;
            }
        }

        return list;
    }

    private static (bool close, bool selfClosing, string name, Dictionary<string, string> attrs) ParseTag(string raw)
    {
        var s = raw.Trim();
        var selfClosing = s.EndsWith('/');
        if (selfClosing)
            s = s[..^1].Trim();
        var close = s.StartsWith('/');
        if (close)
            s = s[1..].Trim();
        if (s.Length == 0)
            return (close, selfClosing, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var space = s.IndexOfAny([' ', '\t', '\n', '\r']);
        var name = (space < 0 ? s : s[..space]).ToLowerInvariant();
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (space > 0)
        {
            foreach (Match m in Regex.Matches(s[space..],
                         @"([a-zA-Z_:][\w:.-]*)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s""'=<>`]+))"))
            {
                var val = m.Groups[3].Success ? m.Groups[3].Value
                    : m.Groups[4].Success ? m.Groups[4].Value
                    : m.Groups[5].Value;
                attrs[m.Groups[1].Value] = WebUtility.HtmlDecode(val);
            }
        }

        return (close, selfClosing, name, attrs);
    }

    private static Paragraph MakeParagraph(List<Run> runs, int heading, int listDepth)
    {
        var pPr = new ParagraphProperties();
        if (heading is >= 1 and <= 6)
        {
            var size = heading switch
            {
                1 => "48",
                2 => "40",
                3 => "32",
                4 => "28",
                5 => "26",
                _ => "24"
            };
            pPr.AppendChild(new SpacingBetweenLines { After = "160", Before = heading == 1 ? "0" : "120" });
            foreach (var run in runs)
            {
                run.RunProperties ??= new RunProperties();
                if (run.RunProperties.Bold is null)
                    run.RunProperties.Bold = new Bold();
                if (run.RunProperties.FontSize is null)
                    run.RunProperties.FontSize = new FontSize { Val = size };
            }
        }
        else
        {
            pPr.AppendChild(new SpacingBetweenLines { After = "120" });
        }

        if (listDepth > 0)
        {
            pPr.AppendChild(new Indentation { Left = (listDepth * 360).ToString(CultureInfo.InvariantCulture) });
            var bullet = new string(' ', (listDepth - 1) * 2) + "• ";
            runs.Insert(0, new Run(new Text(bullet) { Space = SpaceProcessingModeValues.Preserve }));
        }

        var para = new Paragraph(pPr);
        foreach (var run in runs)
            para.AppendChild(run);
        return para;
    }

    private static Table MakeTable(List<List<List<Run>>> rows)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        foreach (var row in rows)
        {
            var tr = new TableRow();
            foreach (var cellRuns in row)
            {
                var para = new Paragraph();
                foreach (var run in cellRuns)
                    para.AppendChild(run);
                var cell = new TableCell(
                    new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
                    para);
                tr.AppendChild(cell);
            }

            table.AppendChild(tr);
        }

        return table;
    }

    private static Run MakeTextRun(string text, InlineFormat format)
    {
        var rPr = new RunProperties();
        if (format.Bold || format.HeadingLevel > 0)
            rPr.AppendChild(new Bold());
        if (format.Italic)
            rPr.AppendChild(new Italic());
        if (format.Underline)
            rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
        if (format.Code)
            rPr.AppendChild(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });

        var run = new Run();
        if (rPr.HasChildren)
            run.AppendChild(rPr);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Run? TryMakeImageRun(Dictionary<string, string> attrs, WriteContext ctx)
    {
        if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
            return null;

        byte[]? bytes = null;
        var contentType = "image/png";
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma < 0)
                    return null;
                var meta = src[5..comma];
                var payload = src[(comma + 1)..];
                var semi = meta.IndexOf(';');
                contentType = semi < 0 ? meta : meta[..semi];
                if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
                    bytes = Convert.FromBase64String(payload);
            }
            else
            {
                var local = ResolveLocalImage(src, ctx.MediaBaseDir);
                if (local is null || !IoFile.Exists(local))
                    return MakeTextRun("[image]", ctx.Format);
                bytes = IoFile.ReadAllBytes(local);
                contentType = GuessMime(local, bytes);
            }
        }
        catch
        {
            return MakeTextRun("[image]", ctx.Format);
        }

        if (bytes is null || bytes.Length == 0)
            return null;

        var partType = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ImagePartType.Jpeg,
            "image/gif" => ImagePartType.Gif,
            "image/bmp" => ImagePartType.Bmp,
            _ => ImagePartType.Png
        };

        var imagePart = ctx.Main.AddImagePart(partType);
        using (var ms = new MemoryStream(bytes))
            imagePart.FeedData(ms);
        var relId = ctx.Main.GetIdOfPart(imagePart);
        var (pxW, pxH) = GuessImageSize(bytes);
        const long emuPerPx = 9525; // 96 dpi
        var cx = Math.Clamp(pxW * emuPerPx, 914400L, 5486400L);
        var cy = Math.Clamp(pxH * emuPerPx, 457200L, 5486400L);
        if (cx == 5486400L && pxW > 0)
            cy = Math.Max(457200L, pxH * 5486400L / pxW);

        var id = ctx.DrawingId++;
        var drawing = new Drawing(
            new Wp.Inline(
                new Wp.Extent { Cx = cx, Cy = cy },
                new Wp.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new Wp.DocProperties { Id = id, Name = "Picture " + id },
                new Wp.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = "img" + id },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        return new Run(drawing);
    }

    private static string? ResolveLocalImage(string src, string? mediaBaseDir)
    {
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(src);
                return uri.LocalPath;
            }
        }
        catch
        {
            return null;
        }

        var trimmed = src.Replace('/', IoPath.DirectorySeparatorChar);
        if (IoPath.IsPathRooted(trimmed) && IoFile.Exists(trimmed))
            return trimmed;
        if (!string.IsNullOrWhiteSpace(mediaBaseDir))
        {
            var combined = IoPath.GetFullPath(IoPath.Combine(mediaBaseDir, src.Replace('/', IoPath.DirectorySeparatorChar)));
            if (IoFile.Exists(combined))
                return combined;
        }

        return null;
    }

    private static string GuessMime(string path, byte[] bytes)
    {
        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/png",
            _ when bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 => "image/jpeg",
            _ => "image/png"
        };
    }

    private static (int w, int h) GuessImageSize(byte[] bytes)
    {
        try
        {
            if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
            {
                var w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                var h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                if (w > 0 && h > 0)
                    return (w, h);
            }

            if (bytes.Length >= 10 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
                return (bytes[6] | (bytes[7] << 8), bytes[8] | (bytes[9] << 8));

            if (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                var i = 2;
                while (i + 8 < bytes.Length)
                {
                    if (bytes[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    var marker = bytes[i + 1];
                    if (marker is 0xC0 or 0xC2)
                    {
                        var h = (bytes[i + 5] << 8) | bytes[i + 6];
                        var w = (bytes[i + 7] << 8) | bytes[i + 8];
                        if (w > 0 && h > 0)
                            return (w, h);
                        break;
                    }

                    if (marker is 0xD8 or 0xD9)
                    {
                        i += 2;
                        continue;
                    }

                    var len = (bytes[i + 2] << 8) | bytes[i + 3];
                    i += 2 + len;
                }
            }
        }
        catch
        {
            // default
        }

        return (480, 320);
    }
}
