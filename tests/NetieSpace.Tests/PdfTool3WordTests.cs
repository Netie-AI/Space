using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Tests;

public class PdfTool3WordTests
{
    private sealed record Para(string Style, string Text, bool HasPicture);

    private sealed record Section(List<Para> Paras, uint WidthTwips, uint HeightTwips);

    /// <summary>The .docx as a person's Word would see it: sections split at section-break paragraphs.</summary>
    private static List<Section> Sections(string docx)
    {
        using var d = WordprocessingDocument.Open(docx, false);
        var body = d.MainDocumentPart!.Document.Body!;
        var result = new List<Section>();
        var cur = new List<Para>();
        foreach (var p in body.Elements<Paragraph>())
        {
            var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";
            var text = string.Concat(p.Descendants<Text>().Select(t => t.Text));
            var pic = p.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Any();
            cur.Add(new Para(style, text, pic));
            var sp = p.ParagraphProperties?.GetFirstChild<SectionProperties>();
            if (sp is not null)
            {
                var ps = sp.GetFirstChild<PageSize>()!;
                result.Add(new Section(cur, ps.Width!.Value, ps.Height!.Value));
                cur = new List<Para>();
            }
        }
        var last = body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!;
        result.Add(new Section(cur, last.Width!.Value, last.Height!.Value));
        return result;
    }

    private static string[] Tokens(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static List<string[]> PdfWordsPerPage(string pdf)
    {
        using var d = PdfKit.Open(pdf);
        return d.GetPages().Select(p => p.GetWords().Select(w => w.Text).ToArray()).ToList();
    }

    [Fact]
    public void Each_page_becomes_a_section_and_its_text_matches_the_pdf_page_by_page()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.Prose(dir, "report.pdf");

        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir()));

        Assert.EndsWith(".docx", docx);
        var sections = Sections(docx);
        var expected = PdfWordsPerPage(pdf);
        Assert.Equal(2, sections.Count);
        for (var i = 0; i < sections.Count; i++)
        {
            var got = Tokens(string.Join(" ", sections[i].Paras.Select(p => p.Text)));
            Assert.Equal(expected[i], got);
        }
        Assert.Contains("caf\u00e9", sections[0].Paras.Select(p => p.Text).Last());
        // Page size carried over: 612 x 792 pt = 12240 x 15840 twips.
        Assert.All(sections, s => { Assert.Equal(12240u, s.WidthTwips); Assert.Equal(15840u, s.HeightTwips); });
    }

    [Fact]
    public void Lines_join_into_paragraphs_by_gap_and_a_large_line_becomes_a_heading()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.Prose(dir, "report.pdf");

        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir()));

        var s1 = Sections(docx)[0].Paras;
        Assert.Equal(3, s1.Count);
        Assert.Equal("Heading1", s1[0].Style);
        Assert.Equal("Annual Report", s1[0].Text);
        Assert.Equal("Normal", s1[1].Style);
        Assert.Equal("The first paragraph starts here and continues on a second line before ending on the third line.", s1[1].Text);
        Assert.StartsWith("A second paragraph follows after a gap and has two lines", s1[2].Text);
        var s2 = Sections(docx)[1].Paras;
        Assert.Equal("Heading2", s2[0].Style);   // 15 pt over 11 pt body is the second heading tier
        Assert.Equal("Second Page", s2[0].Text);
    }

    [Fact]
    public void A_picture_is_carried_over_with_its_pixels_and_sits_between_the_lines_around_it()
    {
        var dir = PdfKit.NewDir();
        var pic = PdfTool3Kit.PhotoPng(dir, "pic.png", 300, 200, 11);
        var pdf = PdfTool3Kit.Prose(dir, "with-pic.pdf", pic);

        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir()));

        var page2 = Sections(docx)[1].Paras;
        Assert.Equal(new[] { "Second Page", "Intro line before the picture.", "", "Closing line after the picture." }, page2.Select(p => p.Text).ToArray());
        Assert.Equal(new[] { false, false, true, false }, page2.Select(p => p.HasPicture).ToArray());

        using var d = WordprocessingDocument.Open(docx, false);
        var part = Assert.Single(d.MainDocumentPart!.ImageParts);
        using var ms = new MemoryStream();
        using (var s = part.GetStream()) s.CopyTo(ms);
        ms.Position = 0;
        using var got = Image.Load<Rgb24>(ms);
        using var original = Image.Load<Rgb24>(pic);
        Assert.Equal(original.Width, got.Width);
        Assert.Equal(original.Height, got.Height);
        Assert.Equal(original[10, 10], got[10, 10]);
        Assert.Equal(original[250, 150], got[250, 150]);
        // Drawn 150 x 100 pt in the PDF: 150 pt = 1905000 EMU in the document.
        var extent = d.MainDocumentPart.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single();
        Assert.Equal(1905000L, extent.Cx!.Value);
        Assert.Equal(1270000L, extent.Cy!.Value);
    }

    [Fact]
    public void A_jpeg_picture_stays_a_jpeg_of_the_same_size()
    {
        var dir = PdfKit.NewDir();
        var pic = PdfTool3Kit.PhotoJpeg(dir, "pic.jpg", 300, 200, 5);
        var pdf = PdfTool3Kit.Prose(dir, "jpg.pdf", pic);

        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir()));

        using var d = WordprocessingDocument.Open(docx, false);
        var part = Assert.Single(d.MainDocumentPart!.ImageParts);
        Assert.Equal("image/jpeg", part.ContentType);
        using var ms = new MemoryStream();
        using (var s = part.GetStream()) s.CopyTo(ms);
        ms.Position = 0;
        using var got = Image.Load<Rgb24>(ms);
        Assert.Equal(300, got.Width);
        Assert.Equal(200, got.Height);
    }

    [Fact]
    public void Pictures_can_be_left_out()
    {
        var dir = PdfKit.NewDir();
        var pic = PdfTool3Kit.PhotoPng(dir, "pic.png", 300, 200);
        var pdf = PdfTool3Kit.Prose(dir, "p.pdf", pic);

        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir(), ("images", "false")));

        using var d = WordprocessingDocument.Open(docx, false);
        Assert.Empty(d.MainDocumentPart!.ImageParts);
        Assert.DoesNotContain(Sections(docx)[1].Paras, p => p.HasPicture);
    }

    [Fact]
    public void A_pdf_without_a_text_layer_is_refused_and_nothing_is_written()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.ImageOnly(dir, "scan.pdf", PdfTool3Kit.PhotoPng(dir, "s.png", 300, 200));
        var outDir = PdfKit.NewDir();

        var r = PdfKit.Run("pdf-to-word", new[] { pdf }, outDir);

        Assert.False(r.Ok);
        Assert.Contains("no extractable text layer", r.Error);
        Assert.Contains("scan.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public void Same_named_inputs_do_not_collide_and_one_bad_file_leaves_no_partial_batch()
    {
        var a = PdfKit.NewDir();
        var b = PdfKit.NewDir();
        var pa = PdfTool3Kit.Prose(a, "same.pdf");
        var pb = PdfTool3Kit.Prose(b, "same.pdf");
        var before = IoFile.ReadAllBytes(pa);
        var outDir = PdfKit.NewDir();

        var outs = PdfKit.RunOk("pdf-to-word", new[] { pa, pb }, outDir);

        Assert.Equal(2, outs.Count);
        Assert.NotEqual(outs[0], outs[1]);
        Assert.Equal(before, IoFile.ReadAllBytes(pa));

        var scan = PdfTool3Kit.ImageOnly(a, "scan.pdf", PdfTool3Kit.PhotoPng(a, "s.png", 300, 200));
        var out2 = PdfKit.NewDir();
        var r = PdfKit.Run("pdf-to-word", new[] { pa, scan }, out2);
        Assert.False(r.Ok);
        Assert.Contains("scan.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(out2));
    }

    [Fact]
    public void A_non_boolean_images_option_is_rejected_naming_the_option()
    {
        var pdf = PdfTool3Kit.Prose(PdfKit.NewDir(), "p.pdf");
        var r = PdfKit.Run("pdf-to-word", new[] { pdf }, PdfKit.NewDir(), ("images", "maybe"));
        Assert.False(r.Ok);
        Assert.Contains("images", r.Error);
    }

    [Fact]
    public void A_landscape_page_keeps_its_orientation()
    {
        var pdf = PdfTool3Kit.Prose(PdfKit.NewDir(), "wide.pdf", null, 842, 595);
        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { pdf }, PdfKit.NewDir()));
        var s = Sections(docx);
        Assert.All(s, x => { Assert.Equal(16840u, x.WidthTwips); Assert.Equal(11900u, x.HeightTwips); });
    }
}
