using NetieSpace.Apps.Pdf;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Tests;

public class PdfToolConvertTests
{
    private static string MakeImage(string dir, string name, int w, int h, Rgba32 color, ushort? exifOrientation = null)
    {
        var path = IoPath.Combine(dir, name);
        using var img = new Image<Rgba32>(w, h, color);
        if (name.EndsWith(".png")) img.SaveAsPng(path);
        else
        {
            if (exifOrientation is ushort o)
            {
                img.Metadata.ExifProfile = new ExifProfile();
                img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, o);
            }
            img.SaveAsJpeg(path, new JpegEncoder { Quality = 90 });
        }
        return path;
    }

    // ----- from images -----

    [Fact]
    public void From_jpg_builds_one_page_per_picture_sized_to_the_picture_by_default()
    {
        var dir = PdfKit.NewDir();
        var a = MakeImage(dir, "a.jpg", 400, 200, new Rgba32(10, 120, 200, 255));
        var b = MakeImage(dir, "b.png", 100, 300, new Rgba32(200, 120, 10, 255));

        var path = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { a, b }, PdfKit.NewDir()));

        using var d = PdfKit.Open(path);
        Assert.Equal(2, d.NumberOfPages);
        var p1 = d.GetPage(1); var p2 = d.GetPage(2);
        Assert.True(p1.Width > p1.Height);
        Assert.True(p2.Height > p2.Width);
        Assert.Equal(300, p1.Width, 1);                      // 400 px at 96 dpi
        var i1 = Assert.Single(p1.GetImages());
        Assert.Equal(400, i1.WidthInSamples);
        Assert.Equal(200, i1.HeightInSamples);
        var i2 = Assert.Single(p2.GetImages());
        Assert.Equal(100, i2.WidthInSamples);
        Assert.Equal(300, i2.HeightInSamples);
    }

    [Fact]
    public void From_jpg_fixed_page_size_orientation_and_margin_are_applied_and_the_picture_stays_inside()
    {
        var dir = PdfKit.NewDir();
        var a = MakeImage(dir, "wide.jpg", 800, 200, new Rgba32(50, 50, 50, 255));

        var portrait = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { a }, PdfKit.NewDir(), ("pagesize", "A4"), ("margin", "big")));
        var landscape = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { a }, PdfKit.NewDir(), ("pagesize", "letter"), ("orientation", "landscape")));

        using (var d = PdfKit.Open(portrait))
        {
            var p = d.GetPage(1);
            Assert.Equal(595.28, p.Width, 0);
            Assert.Equal(841.89, p.Height, 0);
            var b = Assert.Single(p.GetImages()).Bounds;
            Assert.Equal(595.28 - 72, b.Width, 0);           // width limited by the 36pt margins
            Assert.True(b.Left >= 35.5 && b.Right <= 595.28 - 35.5);
        }
        using (var d = PdfKit.Open(landscape))
        {
            var p = d.GetPage(1);
            Assert.Equal(792, p.Width, 0);
            Assert.Equal(612, p.Height, 0);
        }
    }

    [Fact]
    public void From_jpg_unmerged_writes_one_pdf_per_picture_and_same_named_pictures_do_not_collide()
    {
        var a = MakeImage(PdfKit.NewDir("one"), "scan.jpg", 100, 100, new Rgba32(255, 0, 0, 255));
        var b = MakeImage(PdfKit.NewDir("two"), "scan.jpg", 200, 100, new Rgba32(0, 255, 0, 255));
        var outs = PdfKit.RunOk("pdf-from-jpg", new[] { a, b }, PdfKit.NewDir(), ("merge_after", "false"));
        Assert.Equal(2, outs.Count);
        Assert.NotEqual(outs[0], outs[1]);
        Assert.Equal(100, Assert.Single(PdfKit.Open(outs[0]).GetPage(1).GetImages()).WidthInSamples);
        Assert.Equal(200, Assert.Single(PdfKit.Open(outs[1]).GetPage(1).GetImages()).WidthInSamples);
    }

    [Fact]
    public void From_jpg_respects_the_exif_rotation_of_phone_photos()
    {
        var dir = PdfKit.NewDir();
        // Stored 300 wide x 100 high, tagged "rotate 90 clockwise to view": it is shown 100 wide x 300 high.
        var photo = MakeImage(dir, "phone.jpg", 300, 100, new Rgba32(30, 30, 200, 255), exifOrientation: 6);

        var path = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { photo }, PdfKit.NewDir()));

        using var d = PdfKit.Open(path);
        var p = d.GetPage(1);
        Assert.True(p.Height > p.Width, $"page is {p.Width}x{p.Height}");
        var im = Assert.Single(p.GetImages());
        Assert.Equal(100, im.WidthInSamples);
        Assert.Equal(300, im.HeightInSamples);
    }

    [Fact]
    public void From_jpg_rejects_a_file_that_is_not_a_picture_and_names_it()
    {
        var dir = PdfKit.NewDir();
        var fake = IoPath.Combine(dir, "fake.jpg");
        IoFile.WriteAllText(fake, "definitely not a jpeg");
        var outDir = PdfKit.NewDir();
        var r = PdfKit.Run("pdf-from-jpg", new[] { fake }, outDir);
        Assert.False(r.Ok);
        Assert.Contains("fake.jpg", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void From_jpg_runs_through_the_tool_cli()
    {
        var dir = PdfKit.NewDir();
        var a = MakeImage(dir, "a.png", 120, 80, new Rgba32(1, 2, 3, 255));
        var (exit, stdout, err) = PdfKit.Cli("pdf-from-jpg", new[] { a }, PdfKit.NewDir(), ("pagesize", "A4"));
        Assert.True(exit == 0, err);
        Assert.Equal(1, PdfKit.PageCount(Assert.Single(PdfKit.Lines(stdout))));
    }

    // ----- compare -----

    private static string MakeLines(string dir, string name, params string[][] pages)
    {
        using var doc = new PdfDocument();
        foreach (var lines in pages)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            var f = new XFont("Arial", 12);
            for (var i = 0; i < lines.Length; i++)
                g.DrawString(lines[i], f, XBrushes.Black, new XPoint(60, 80 + i * 24));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    [Fact]
    public void Compare_reports_exactly_the_lines_that_changed_with_their_pages()
    {
        var dir = PdfKit.NewDir();
        var a = MakeLines(dir, "v1.pdf",
            new[] { "Invoice number 1001", "Total due 500 dollars", "Thank you" },
            new[] { "Terms net thirty days", "Late fee two percent" });
        var b = MakeLines(dir, "v2.pdf",
            new[] { "Invoice number 1001", "Total due 750 dollars", "Thank you" },
            new[] { "Terms net thirty days", "Late fee two percent", "Signed by the client" });

        var path = Assert.Single(PdfKit.RunOk("pdf-compare", new[] { a, b }, PdfKit.NewDir()));

        var report = IoFile.ReadAllText(path);
        Assert.EndsWith(".md", path);
        Assert.Contains("- Lines removed: 1", report);
        Assert.Contains("- Lines added: 2", report);
        Assert.Contains("- (page 1) Total due 500 dollars", report);
        Assert.Contains("+ (page 1) Total due 750 dollars", report);
        Assert.Contains("+ (page 2) Signed by the client", report);
        Assert.DoesNotContain("Invoice number 1001\n+", report);
        Assert.DoesNotContain("(page 1) Thank you", report);
    }

    [Fact]
    public void Compare_of_two_files_with_the_same_text_says_so_and_needs_exactly_two_inputs()
    {
        var dir = PdfKit.NewDir();
        var a = MakeLines(dir, "a.pdf", new[] { "same words here" });
        var b = MakeLines(PdfKit.NewDir(), "a.pdf", new[] { "same words here" });
        var report = IoFile.ReadAllText(Assert.Single(PdfKit.RunOk("pdf-compare", new[] { a, b }, PdfKit.NewDir())));
        Assert.Contains("identical text", report);
        Assert.Contains("- Lines removed: 0", report);

        var one = PdfKit.Run("pdf-compare", new[] { a }, PdfKit.NewDir());
        Assert.False(one.Ok);
    }

    [Fact]
    public void Compare_of_the_same_name_in_two_folders_is_named_apart_from_both_inputs()
    {
        var a = MakeLines(PdfKit.NewDir("x"), "same.pdf", new[] { "alpha" });
        var b = MakeLines(PdfKit.NewDir("y"), "same.pdf", new[] { "beta" });
        var outDir = PdfKit.NewDir();
        var path = Assert.Single(PdfKit.RunOk("pdf-compare", new[] { a, b }, outDir));
        Assert.Equal(outDir, IoPath.GetDirectoryName(path));
        Assert.Contains("- alpha", IoFile.ReadAllText(path).Replace("(page 1) ", ""));
    }

    [Fact]
    public void Diff_finds_a_minimal_edit_script()
    {
        var a = "a b c d e f".Split(' ').ToList();
        var b = "a c d x e f g".Split(' ').ToList();
        var ops = PdfTextTools.Diff(a, b, CancellationToken.None);
        Assert.Equal(new[] { "-b", "+x", "+g" }.OrderBy(x => x),
            ops.Where(o => o.Kind != '=').Select(o => o.Kind + (o.Kind == '-' ? a[o.Index] : b[o.Index])).OrderBy(x => x));
        Assert.Empty(PdfTextTools.Diff(new List<string>(), new List<string>(), CancellationToken.None));
        Assert.Equal(3, PdfTextTools.Diff(new List<string>(), new List<string> { "1", "2", "3" }, CancellationToken.None).Count(o => o.Kind == '+'));
    }

    [Fact]
    public void Compare_runs_through_the_tool_cli()
    {
        var dir = PdfKit.NewDir();
        var a = MakeLines(dir, "a.pdf", new[] { "one two three" });
        var b = MakeLines(dir, "b.pdf", new[] { "one two four" });
        var (exit, stdout, err) = PdfKit.Cli("pdf-compare", new[] { a, b }, PdfKit.NewDir());
        Assert.True(exit == 0, err);
        var report = IoFile.ReadAllText(Assert.Single(PdfKit.Lines(stdout)));
        Assert.Contains("- (page 1) one two three", report);
        Assert.Contains("+ (page 1) one two four", report);
    }

    // ----- to markdown -----

    private static string MakeArticle(string dir)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page))
        {
            var h1 = new XFont("Arial", 24, XFontStyle.Bold);
            var h2 = new XFont("Arial", 16, XFontStyle.Bold);
            var body = new XFont("Arial", 11);
            g.DrawString("Quarterly Review", h1, XBrushes.Black, new XPoint(60, 80));
            g.DrawString("First paragraph line one that goes on for a while", body, XBrushes.Black, new XPoint(60, 110));
            g.DrawString("and continues on a second line of the same paragraph.", body, XBrushes.Black, new XPoint(60, 124));
            g.DrawString("Highlights", h2, XBrushes.Black, new XPoint(60, 170));
            g.DrawString("- Revenue grew steadily", body, XBrushes.Black, new XPoint(60, 196));
            g.DrawString("- Costs stayed flat", body, XBrushes.Black, new XPoint(60, 210));
            g.DrawString("1. Hire two engineers", body, XBrushes.Black, new XPoint(60, 240));
            g.DrawString("2. Ship the beta", body, XBrushes.Black, new XPoint(60, 254));
            g.DrawString("A separate closing paragraph after a gap.", body, XBrushes.Black, new XPoint(60, 300));
        }
        var path = IoPath.Combine(dir, "article.pdf");
        doc.Save(path);
        return path;
    }

    [Fact]
    public void To_markdown_recovers_headings_lists_and_paragraphs_from_font_sizes_and_spacing()
    {
        var src = MakeArticle(PdfKit.NewDir());

        var path = Assert.Single(PdfKit.RunOk("pdf-to-markdown", new[] { src }, PdfKit.NewDir()));

        Assert.EndsWith(".md", path);
        var md = IoFile.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("# Quarterly Review\n", md);
        Assert.Contains("## Highlights\n", md);
        Assert.Contains("First paragraph line one that goes on for a while and continues on a second line of the same paragraph.\n", md);
        Assert.Contains("- Revenue grew steadily\n- Costs stayed flat\n", md);
        Assert.Contains("1. Hire two engineers\n2. Ship the beta\n", md);
        Assert.Contains("\nA separate closing paragraph after a gap.\n", md);
        Assert.True(md.IndexOf("# Quarterly Review") < md.IndexOf("## Highlights"));
    }

    [Fact]
    public void To_markdown_converts_the_real_sample_report_to_non_empty_text_that_matches_its_extractable_words()
    {
        var src = ToolTestSupport.FixturePdf();
        var path = Assert.Single(PdfKit.RunOk("pdf-to-markdown", new[] { src }, PdfKit.NewDir()));
        var md = IoFile.ReadAllText(path);
        Assert.True(md.Length > 100);
        using var pig = PdfKit.Open(src);
        var words = pig.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).Where(w => w.Length > 5 && w.All(char.IsLetter)).Distinct().Take(10).ToList();
        Assert.NotEmpty(words);
        foreach (var w in words) Assert.Contains(w, md);
    }

    [Fact]
    public void To_markdown_refuses_a_pdf_without_a_text_layer_instead_of_writing_an_empty_file()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        var blank = IoPath.Combine(PdfKit.NewDir(), "blank.pdf");
        doc.Save(blank);
        var outDir = PdfKit.NewDir();
        var r = PdfKit.Run("pdf-to-markdown", new[] { blank }, outDir);
        Assert.False(r.Ok);
        Assert.Contains("no extractable text", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void To_markdown_runs_through_the_tool_cli_for_several_files()
    {
        var a = MakeArticle(PdfKit.NewDir("one"));
        var b = MakeArticle(PdfKit.NewDir("two"));
        var (exit, stdout, err) = PdfKit.Cli("pdf-to-markdown", new[] { a, b }, PdfKit.NewDir());
        Assert.True(exit == 0, err);
        var lines = PdfKit.Lines(stdout);
        Assert.Equal(2, lines.Length);
        Assert.NotEqual(lines[0], lines[1]);
        Assert.All(lines, l => Assert.Contains("# Quarterly Review", IoFile.ReadAllText(l)));
    }
}
