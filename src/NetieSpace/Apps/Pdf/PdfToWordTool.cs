using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NetieSpace.Apps.Tools;
using UglyToad.PdfPig.Content;
using static NetieSpace.Apps.Pdf.PdfToolSupport;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// pdf-to-word: a .docx with one section per PDF page. Text is reflowed, not laid out: words are grouped into
/// lines by baseline and lines into paragraphs by vertical gap; a line set noticeably larger than the body
/// text becomes a heading; pictures PdfPig can expose are placed in reading order. Columns, tables, text boxes,
/// fonts, colours and exact positions are not reproduced. A PDF with no text layer is refused.
/// </summary>
internal static class PdfToWordTool
{
    private const int MarginTwips = 1080;

    public static Task<IReadOnlyList<string>> RunAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var withImages = OptBool(req, "images", true);
        return PdfTool3Batch.Run(req, progress, ct, "", ".docx", (input, output, token) => Convert(input, output, withImages, token));
    }

    private abstract record Block(double Top);
    private sealed record TextBlock(double Top, int Heading, string Text) : Block(Top);
    private sealed record ImageBlock(double Top, byte[] Bytes, bool Jpeg, double W, double H) : Block(Top);

    private static void Convert(string input, string output, bool withImages, CancellationToken ct)
    {
        var pages = new List<(double W, double H, List<Block> Blocks)>();
        UglyToad.PdfPig.PdfDocument doc;
        try { doc = UglyToad.PdfPig.PdfDocument.Open(input); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"cannot read as a PDF: {ex.Message} (password protected files need pdf-unlock first; damaged files need pdf-repair)", ex);
        }
        using (doc)
        {
            var perPage = new List<(Page Page, List<PdfTextLine> Lines)>();
            for (var n = 1; n <= doc.NumberOfPages; n++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.GetPage(n);
                perPage.Add((page, PdfText.Lines(page.GetWords())));
            }
            if (perPage.All(p => p.Lines.Count == 0))
                throw new InvalidOperationException("no extractable text layer (scanned PDF? use pdf-ocr first)");

            var body = BodySize(perPage.SelectMany(p => p.Lines));
            foreach (var (page, lines) in perPage)
            {
                ct.ThrowIfCancellationRequested();
                var blocks = new List<Block>();
                blocks.AddRange(Paragraphs(lines, body));
                if (withImages) blocks.AddRange(Pictures(page));
                var w = page.Width; var h = page.Height;
                // PdfPig already reports width and height as displayed (swapped for /Rotate 90 and 270).
                pages.Add((w, h, blocks.OrderByDescending(b => b.Top).ToList()));
            }
        }
        Write(output, pages, ct);
    }

    private static double BodySize(IEnumerable<PdfTextLine> lines)
    {
        var weight = new Dictionary<double, int>();
        foreach (var l in lines)
        {
            var key = Math.Round(l.Size * 2) / 2;
            weight[key] = weight.GetValueOrDefault(key) + l.Text.Length;
        }
        return weight.Count == 0 ? 11 : weight.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    private static int HeadingLevel(PdfTextLine l, double body)
    {
        if (l.Text.Length > 160) return 0;
        var ratio = l.Size / body;
        if (ratio >= 1.6) return 1;
        if (ratio >= 1.3) return 2;
        if (ratio >= 1.15) return 3;
        return 0;
    }

    private static List<Block> Paragraphs(List<PdfTextLine> lines, double body)
    {
        var result = new List<Block>();
        var text = new List<string>();
        var level = 0;
        double top = 0, lastBaseline = 0, lastSize = 0;
        var open = false;

        void Flush()
        {
            if (open) result.Add(new TextBlock(top, level, string.Join(" ", text)));
            text.Clear();
            open = false;
        }

        foreach (var l in lines)
        {
            var lvl = HeadingLevel(l, body);
            var gap = open ? lastBaseline - l.Baseline : 0;
            var newPara = !open || lvl != level || gap > 1.65 * Math.Max(lastSize, l.Size) || (lvl > 0 && gap > 1.35 * Math.Max(lastSize, l.Size));
            if (newPara)
            {
                Flush();
                open = true; level = lvl; top = l.Top;
            }
            text.Add(l.Text);
            lastBaseline = l.Baseline;
            lastSize = l.Size;
        }
        Flush();
        return result;
    }

    private static IEnumerable<Block> Pictures(Page page)
    {
        IEnumerable<IPdfImage> images;
        try { images = page.GetImages().ToList(); }
        catch { yield break; }
        foreach (var im in images)
        {
            byte[]? bytes = null;
            var jpeg = false;
            try
            {
                var raw = im.RawBytes.ToArray();
                if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8) { bytes = raw; jpeg = true; }
                else if (im.TryGetPng(out var png) && png is { Length: > 0 }) bytes = png;
            }
            catch { /* an image PdfPig cannot decode is simply not placed */ }
            if (bytes is null) continue;
            var b = im.Bounds;
            if (b.Width < 1 || b.Height < 1) continue;
            yield return new ImageBlock(b.Top, bytes, jpeg, b.Width, b.Height);
        }
    }

    // ----------------------------------------------------------------------------------------------

    private static void Write(string path, List<(double W, double H, List<Block> Blocks)> pages, CancellationToken ct)
    {
        using (var pkg = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = pkg.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            main.Document = new Document(body);
            uint picId = 1;

            for (var p = 0; p < pages.Count; p++)
            {
                ct.ThrowIfCancellationRequested();
                var (w, h, blocks) = pages[p];
                var contentW = Math.Max(72.0, w - 2 * MarginTwips / 20.0);
                var added = new List<Paragraph>();
                foreach (var b in blocks)
                {
                    if (b is TextBlock t)
                    {
                        var para = new Paragraph();
                        if (t.Heading > 0)
                            para.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = "Heading" + t.Heading });
                        para.Append(new Run(new Text(t.Text) { Space = SpaceProcessingModeValues.Preserve }));
                        added.Add(para);
                    }
                    else if (b is ImageBlock im)
                    {
                        var scale = Math.Min(1.0, contentW / im.W);
                        var ip = main.AddImagePart(im.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png);
                        using (var ms = new System.IO.MemoryStream(im.Bytes)) ip.FeedData(ms);
                        var id = main.GetIdOfPart(ip);
                        added.Add(new Paragraph(new Run(Drawing(id, picId++, (long)(im.W * scale * 12700), (long)(im.H * scale * 12700)))));
                    }
                }
                if (added.Count == 0) added.Add(new Paragraph());

                var sect = new SectionProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.PageSize
                    {
                        Width = (uint)Math.Max(1, Math.Round(w * 20)),
                        Height = (uint)Math.Max(1, Math.Round(h * 20)),
                        Orient = w > h ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
                    },
                    new PageMargin { Top = MarginTwips, Bottom = MarginTwips, Left = (uint)MarginTwips, Right = (uint)MarginTwips, Header = 720, Footer = 720, Gutter = 0 });
                foreach (var para in added.Take(added.Count - 1)) body.Append(para);
                var last = added[^1];
                if (p < pages.Count - 1)
                {
                    // A section break rides on the last paragraph of every page but the final one.
                    last.ParagraphProperties ??= new ParagraphProperties();
                    last.ParagraphProperties.Append(sect);
                    body.Append(last);
                }
                else
                {
                    body.Append(last);
                    body.Append(sect);
                }
            }
            main.Document.Save();
        }
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var sp = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new FontSize { Val = "22" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "120", Line = "264", LineRule = LineSpacingRuleValues.Auto }))));
        styles.Append(new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
        var sizes = new[] { "36", "30", "26" };
        for (var i = 1; i <= 3; i++)
        {
            styles.Append(new Style(
                new StyleName { Val = "heading " + i },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "80" }, new OutlineLevel { Val = i - 1 }),
                new StyleRunProperties(new Bold(), new FontSize { Val = sizes[i - 1] }))
            { Type = StyleValues.Paragraph, StyleId = "Heading" + i });
        }
        sp.Styles = styles;
    }

    private static Drawing Drawing(string relId, uint id, long cx, long cy)
    {
        cx = Math.Max(cx, 12700); cy = Math.Max(cy, 12700);
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.DocProperties { Id = id, Name = "Picture " + id },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "image" + id },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }
}
