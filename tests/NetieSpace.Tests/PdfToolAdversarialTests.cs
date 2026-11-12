using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PdfSharpCore.Drawing;
using Sharp = PdfSharpCore.Pdf;

namespace NetieSpace.Tests;

/// <summary>Verifier attacks on the PDF runners: hostile names and text, odd shapes, corrupt input, same-name inputs.</summary>
public class PdfToolAdversarialTests
{
    private static string Img(string dir, string name, int w, int h, bool png = true)
    {
        var p = IoPath.Combine(dir, name);
        using var i = new Image<Rgba32>(w, h, new Rgba32(10, 200, 30, 128));
        if (png) i.SaveAsPng(p); else i.SaveAsJpeg(p);
        return p;
    }

    [Theory]
    [InlineData("Caf\u00E9 \u00DCber")]
    [InlineData("A'B \"C\" 50% D:E;F\\G/H")]
    public void Watermark_text_survives_non_ascii_and_hostile_text(string text)
    {
        var f = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1);
        var o = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { f }, PdfKit.NewDir(), ("text", text), ("rotation", "0")));
        Assert.Contains(text, PdfKit.Texts(o)[0]);
    }

    [Theory]
    [InlineData("pdf-rotate")]
    [InlineData("pdf-page-numbers")]
    [InlineData("pdf-watermark")]
    [InlineData("pdf-to-markdown")]
    [InlineData("pdf-extract-pages")]
    [InlineData("pdf-remove-pages")]
    public void Same_named_inputs_odd_names_and_missing_outdir(string id)
    {
        var name = "Bob's r\u00E9sum\u00E9 #1 (final) %20.pdf";
        var a = PdfKit.Make(PdfKit.NewDir("x"), name, 3, "AAA");
        var b = PdfKit.Make(PdfKit.NewDir("y"), name, 3, "BBB");
        var outDir = IoPath.Combine(PdfKit.NewDir(), "does", "not", "exist");
        var single = id is "pdf-extract-pages" or "pdf-remove-pages";
        var opts = single ? new[] { ("pages", "1") } : Array.Empty<(string, string)>();
        var files = single ? new[] { a } : new[] { a, b };
        var outs = PdfKit.RunOk(id, files, outDir, opts);
        Assert.Equal(outs.Count, outs.Distinct().Count());
        Assert.All(outs, o => Assert.StartsWith(outDir, o));
        Assert.All(outs, o => Assert.True(IoFile.Exists(o)));
        if (files.Length == 2)
        {
            Assert.Equal(2, outs.Count);
            var all = string.Join("|", outs.Select(o => o.EndsWith(".md") ? IoFile.ReadAllText(o) : string.Join(" ", PdfKit.Texts(o))));
            Assert.Contains("AAA", all);
            Assert.Contains("BBB", all);
        }
        Assert.Equal(3, PdfKit.PageCount(a));
    }

    [Theory]
    [InlineData("pdf-merge")]
    [InlineData("pdf-split")]
    [InlineData("pdf-rotate")]
    [InlineData("pdf-crop")]
    [InlineData("pdf-page-numbers")]
    [InlineData("pdf-watermark")]
    [InlineData("pdf-protect")]
    [InlineData("pdf-unlock")]
    [InlineData("pdf-compare")]
    [InlineData("pdf-to-markdown")]
    [InlineData("pdf-organize")]
    [InlineData("pdf-extract-pages")]
    [InlineData("pdf-remove-pages")]
    public void Corrupt_and_zero_byte_inputs_fail_clearly_and_leave_nothing(string id)
    {
        var good = PdfKit.Make(PdfKit.NewDir(), "good.pdf", 2);
        foreach (var bad in new[] { new byte[0], System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 garbage garbage") })
        {
            var badPath = IoPath.Combine(PdfKit.NewDir(), "bad.pdf");
            IoFile.WriteAllBytes(badPath, bad);
            var outDir = PdfKit.NewDir();
            var files = id is "pdf-merge" or "pdf-compare" ? new[] { good, badPath } : new[] { badPath };
            var opts = id == "pdf-protect" ? new[] { ("password", "pw") } :
                id == "pdf-crop" ? new[] { ("left", "10") } :
                id is "pdf-extract-pages" or "pdf-remove-pages" ? new[] { ("pages", "1") } :
                id == "pdf-split" ? new[] { ("ranges", "1") } : Array.Empty<(string, string)>();
            var r = PdfKit.Run(id, files, outDir, opts);
            Assert.False(r.Ok, id + " succeeded on a corrupt file");
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            Assert.Empty(IoDirectory.GetFiles(outDir));
        }
    }

    [Fact]
    public void Merge_keeps_rotation_of_source_pages_and_accepts_same_file_twice()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 2, "A", tweak: (p, i) => { if (i == 2) p.Rotate = 90; });
        var o = Assert.Single(PdfKit.RunOk("pdf-merge", new[] { a, a }, PdfKit.NewDir()));
        Assert.Equal(new List<int> { 0, 90, 0, 90 }, PdfKit.Rotations(o));
        Assert.Equal(new[] { "A page 1", "A page 2", "A page 1", "A page 2" }, PdfKit.Texts(o).ToArray());
    }

    [Fact]
    public void Merge_with_a_protected_input_fails_rather_than_blank_pages()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 2, "A");
        var p = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { a }, PdfKit.NewDir(), ("password", "pw")));
        var r = PdfKit.Run("pdf-merge", new[] { a, p }, PdfKit.NewDir());
        if (r.Ok) Assert.Equal(new[] { "A page 1", "A page 2", "A page 1", "A page 2" }, PdfKit.Texts(r.Outputs[0]).ToArray());
    }

    [Fact]
    public void Protect_unlock_roundtrip_wrong_password_and_unicode_password()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 3, "SECRET");
        var expect = new[] { "SECRET page 1", "SECRET page 2", "SECRET page 3" };
        foreach (var pw in new[] { "pw", "p\u00E4ss w\u00F6rd'\"%:", "x" })
        {
            var p = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { a }, PdfKit.NewDir(), ("password", pw)));
            Assert.ThrowsAny<Exception>(() => PdfKit.Texts(p));
            Assert.ThrowsAny<Exception>(() => PdfKit.Texts(p, "wrong"));
            Assert.Equal(expect, PdfKit.Texts(p, pw).ToArray());
            Assert.False(PdfKit.Run("pdf-unlock", new[] { p }, PdfKit.NewDir(), ("password", pw + "z")).Ok);
            Assert.False(PdfKit.Run("pdf-unlock", new[] { p }, PdfKit.NewDir()).Ok);
            var u = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { p }, PdfKit.NewDir(), ("password", pw)));
            Assert.Equal(expect, PdfKit.Texts(u).ToArray());
        }
    }

    [Fact]
    public void Protect_already_protected_and_unlock_plain_file_are_honest()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1, "S");
        var p = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { a }, PdfKit.NewDir(), ("password", "pw")));
        var again = PdfKit.Run("pdf-protect", new[] { p }, PdfKit.NewDir(), ("password", "pw2"));
        if (again.Ok) Assert.Contains("S page 1", PdfKit.Texts(again.Outputs[0], "pw2")[0]);
        var u = PdfKit.Run("pdf-unlock", new[] { a }, PdfKit.NewDir());
        if (u.Ok) Assert.Contains("S page 1", PdfKit.Texts(u.Outputs[0])[0]);
    }

    [Fact]
    public void Protect_and_unlock_keep_images()
    {
        var img = Img(PdfKit.NewDir(), "i.png", 50, 40);
        var pdf = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { img }, PdfKit.NewDir()));
        var p = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { pdf }, PdfKit.NewDir(), ("password", "pw")));
        using (var d = PdfKit.Open(p, "pw")) Assert.Single(d.GetPage(1).GetImages());
        var u = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { p }, PdfKit.NewDir(), ("password", "pw")));
        using var d2 = PdfKit.Open(u);
        Assert.Single(d2.GetPage(1).GetImages());
    }

    [Fact]
    public void Crop_twice_on_a_rotated_page_accumulates_and_big_margins_fail()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1, "C", tweak: (p, i) => p.Rotate = 90);
        var o = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { a }, PdfKit.NewDir(), ("left", "10"), ("top", "20"), ("right", "30"), ("bottom", "40")));
        var b = PdfKit.CropBoxes(o)[0];
        // rotation 90: displayed left/right shrink the unrotated height (y), displayed top/bottom shrink unrotated width (x)
        Assert.Equal(792 - 10 - 30, b[3] - b[1], 1);
        Assert.Equal(612 - 20 - 40, b[2] - b[0], 1);
        var o2 = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { o }, PdfKit.NewDir(), ("left", "10")));
        var b2 = PdfKit.CropBoxes(o2)[0];
        Assert.Equal(792 - 10 - 30 - 10, b2[3] - b2[1], 1);
        Assert.False(PdfKit.Run("pdf-crop", new[] { a }, PdfKit.NewDir(), ("left", "500"), ("right", "400")).Ok);
    }

    private static string MkLines(string dir, string name, List<string> ls)
    {
        using var doc = new Sharp.PdfDocument();
        var pg = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(pg))
            for (var i = 0; i < ls.Count; i++) g.DrawString("line" + ls[i], new XFont("Arial", 10), XBrushes.Black, new XPoint(40, 40 + i * 20));
        var p = IoPath.Combine(dir, name);
        doc.Save(p);
        return p;
    }

    [Fact]
    public void Compare_reports_exactly_the_edit_distance_for_random_files()
    {
        var dir = PdfKit.NewDir();
        var rnd = new Random(5);
        for (var trial = 0; trial < 30; trial++)
        {
            var la = Enumerable.Range(0, rnd.Next(1, 12)).Select(_ => "w" + rnd.Next(6)).ToList();
            var lb = Enumerable.Range(0, rnd.Next(1, 12)).Select(_ => "w" + rnd.Next(6)).ToList();
            var o = Assert.Single(PdfKit.RunOk("pdf-compare", new[] { MkLines(dir, $"a{trial}.pdf", la), MkLines(dir, $"b{trial}.pdf", lb) }, PdfKit.NewDir()));
            var md = IoFile.ReadAllText(o);
            int n = la.Count, m = lb.Count;
            var L = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    L[i, j] = la[i] == lb[j] ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
            Assert.Contains($"- Lines removed: {n - L[0, 0]}", md);
            Assert.Contains($"- Lines added: {m - L[0, 0]}", md);
            Assert.Equal(n - L[0, 0], md.Split('\n').Count(l => l.StartsWith("- (page")));
            Assert.Equal(m - L[0, 0], md.Split('\n').Count(l => l.StartsWith("+ (page")));
        }
    }

    [Fact]
    public void From_jpg_odd_formats_and_sizes()
    {
        var dir = PdfKit.NewDir();
        var one = Img(dir, "one.png", 1, 1);
        var big = Img(dir, "big.jpg", 4000, 3000, png: false);
        var bmpP = IoPath.Combine(dir, "x.bmp");
        using (var i = new Image<Rgba32>(30, 20, new Rgba32(1, 2, 3, 255))) i.SaveAsBmp(bmpP);
        var o = Assert.Single(PdfKit.RunOk("pdf-from-jpg", new[] { one, big, bmpP }, PdfKit.NewDir(), ("pagesize", "A4"), ("margin", "small")));
        using var d = PdfKit.Open(o);
        Assert.Equal(3, d.NumberOfPages);
        for (var n = 1; n <= 3; n++) Assert.Single(d.GetPage(n).GetImages());
        var tif = IoPath.Combine(dir, "t.tif");
        IoFile.WriteAllBytes(tif, new byte[] { 0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 0, 0 });
        var r = PdfKit.Run("pdf-from-jpg", new[] { tif }, PdfKit.NewDir());
        if (r.Ok) { using var dt = PdfKit.Open(r.Outputs[0]); Assert.Single(dt.GetPage(1).GetImages()); }
        else Assert.Contains("t.tif", r.Error);
    }

    [Fact]
    public void Pre_cancelled_token_stops_runners_without_output()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 3);
        var b = PdfKit.Make(PdfKit.NewDir(), "b.pdf", 3);
        foreach (var id in new[] { "pdf-merge", "pdf-rotate", "pdf-compare", "pdf-to-markdown" })
        {
            var tool = PdfKit.Registry.Find(id)!;
            var outDir = PdfKit.NewDir();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var req = new ToolRequest(new[] { a, b }.ToList(), outDir, new Dictionary<string, string>());
            Exception? ex = null; ToolResult? r = null;
            try { r = ToolRunner.RunAsync(tool, req, null, cts.Token).GetAwaiter().GetResult(); } catch (Exception e) { ex = e; }
            Assert.True(ex is OperationCanceledException || (r is not null && !r.Ok), id);
            Assert.Empty(IoDirectory.GetFiles(outDir));
        }
    }

    [Fact]
    public void Page_numbers_n_of_total_subset_and_start_zero()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 5, "N");
        var o = Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { a }, PdfKit.NewDir(), ("pages", "2-4"), ("format", "n-of-total"), ("start", "0")));
        var t = PdfKit.Texts(o);
        Assert.DoesNotContain(" of ", t[0]);
        Assert.DoesNotContain(" of ", t[4]);
        Assert.Contains("0 of 2", t[1]);
        Assert.Contains("1 of 2", t[2]);
        Assert.Contains("2 of 2", t[3]);
    }

    [Fact]
    public void Split_modes_cover_every_page_once()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 7, "S", extraLines: 40);
        var outs = PdfKit.RunOk("pdf-split", new[] { a }, PdfKit.NewDir(), ("split_mode", "fixed_range"), ("every", "3"));
        Assert.Equal(new[] { 3, 3, 1 }, outs.Select(o => PdfKit.PageCount(o)).ToArray());
        var fs = PdfKit.RunOk("pdf-split", new[] { a }, PdfKit.NewDir(), ("split_mode", "filesize"), ("max_size_kb", "40"));
        var all = fs.SelectMany(o => PdfKit.Texts(o)).Select(t => t.Substring(0, 8)).ToList();
        Assert.Equal(Enumerable.Range(1, 7).Select(i => "S page " + i).ToList(), all);
        foreach (var o in fs) Assert.True(new FileInfo(o).Length <= 40 * 1024, o);
    }

    [Fact]
    public void Organize_blank_rotation_and_cross_file_order()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 3, "A");
        var b = PdfKit.Make(PdfKit.NewDir(), "b.pdf", 2, "B", width: 300, height: 200);
        var o = Assert.Single(PdfKit.RunOk("pdf-organize", new[] { a, b }, PdfKit.NewDir(), ("plan", "2:2;1:3;blank;2:1@90;1:1-2")));
        Assert.Equal(new[] { "B page 2", "A page 3", "", "B page 1", "A page 1", "A page 2" }, PdfKit.Texts(o).ToArray());
        Assert.Equal(new List<int> { 0, 0, 0, 90, 0, 0 }, PdfKit.Rotations(o));
    }

    [Fact]
    public void Options_at_and_beyond_limits_are_rejected_naming_the_option()
    {
        var a = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 2);
        foreach (var (id, k, v) in new[] { ("pdf-watermark", "opacity", "0"), ("pdf-watermark", "opacity", "101"), ("pdf-watermark", "rotation", "361"), ("pdf-watermark", "size", "5"),
                     ("pdf-page-numbers", "size", "3"), ("pdf-page-numbers", "start", "-1"), ("pdf-rotate", "pages", "3"), ("pdf-rotate", "pages", "0"), ("pdf-rotate", "pages", "2-1"), ("pdf-page-numbers", "color", "red") })
        {
            var r = PdfKit.Run(id, new[] { a }, PdfKit.NewDir(), (k, v));
            Assert.False(r.Ok, id + k + v);
            Assert.Contains(k, r.Error);
        }
        foreach (var (k, v) in new[] { ("opacity", "1"), ("opacity", "100"), ("rotation", "0"), ("rotation", "360"), ("size", "6"), ("size", "400") })
            PdfKit.RunOk("pdf-watermark", new[] { a }, PdfKit.NewDir(), (k, v));
    }

    [Fact]
    public void Watermark_image_mode_embeds_picture_and_rejects_non_image()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "W");
        var img = Img(dir, "wm.png", 80, 40);
        var o = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { a }, PdfKit.NewDir(), ("mode", "image"), ("image", img), ("tile", "true")));
        using var d = PdfKit.Open(o);
        Assert.True(d.GetPage(1).GetImages().Any());
        Assert.Equal("W page 1", d.GetPage(1).Text.Trim());
        Assert.False(PdfKit.Run("pdf-watermark", new[] { a }, PdfKit.NewDir(), ("mode", "image"), ("image", a)).Ok);
    }

    [Fact]
    public void To_markdown_reads_headings_and_keeps_order_on_two_pages()
    {
        var dir = PdfKit.NewDir();
        using var doc = new Sharp.PdfDocument();
        for (var p = 1; p <= 2; p++)
        {
            var pg = doc.AddPage();
            using var g = XGraphics.FromPdfPage(pg);
            g.DrawString("Title " + p, new XFont("Arial", 24, XFontStyle.Bold), XBrushes.Black, new XPoint(50, 60));
            for (var i = 0; i < 6; i++) g.DrawString($"body text number {p}-{i} with some words", new XFont("Arial", 11), XBrushes.Black, new XPoint(50, 100 + i * 14));
            g.DrawString("- bullet " + p, new XFont("Arial", 11), XBrushes.Black, new XPoint(50, 220));
        }
        var f = IoPath.Combine(dir, "d.pdf");
        doc.Save(f);
        var md = IoFile.ReadAllText(Assert.Single(PdfKit.RunOk("pdf-to-markdown", new[] { f }, PdfKit.NewDir())));
        Assert.Contains("# Title 1", md);
        Assert.Contains("# Title 2", md);
        Assert.True(md.IndexOf("Title 1") < md.IndexOf("1-5") && md.IndexOf("1-5") < md.IndexOf("Title 2"));
        Assert.Contains("- bullet 1", md);
    }

    [Fact]
    public void Watermark_text_outside_latin1_is_refused_not_stamped_as_wrong_glyphs()
    {
        var f = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1);
        var r = PdfKit.Run("pdf-watermark", new[] { f }, PdfKit.NewDir(), ("text", "\u4f60\u597d"), ("rotation", "0"));
        Assert.False(r.Ok);
        Assert.Contains("text", r.Error);
    }

    [Fact]
    public void Protect_permissions_are_written_into_the_file()
    {
        var f = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1);
        var o = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { f }, PdfKit.NewDir(), ("password", "pw"), ("allow_copy", "false"), ("allow_print", "false"), ("allow_modify", "false")));
        var m = System.Text.RegularExpressions.Regex.Match(PdfKit.Latin1(o), @"/P (-?\d+)");
        Assert.True(m.Success);
        var perm = int.Parse(m.Groups[1].Value);
        Assert.Equal(0, perm & 16);   // copy
        Assert.Equal(0, perm & 4);    // print
        Assert.Equal(0, perm & 8);    // modify
        var o2 = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { f }, PdfKit.NewDir(), ("password", "pw"), ("allow_copy", "true"), ("allow_print", "true"), ("allow_modify", "true")));
        var perm2 = int.Parse(System.Text.RegularExpressions.Regex.Match(PdfKit.Latin1(o2), @"/P (-?\d+)").Groups[1].Value);
        Assert.NotEqual(0, perm2 & 16);
        Assert.NotEqual(0, perm2 & 4);
        Assert.NotEqual(0, perm2 & 8);
    }

    [Theory]
    [InlineData("pdf-rotate")]
    [InlineData("pdf-page-numbers")]
    [InlineData("pdf-watermark")]
    [InlineData("pdf-crop")]
    [InlineData("pdf-protect")]
    [InlineData("pdf-unlock")]
    [InlineData("pdf-to-markdown")]
    [InlineData("pdf-merge")]
    [InlineData("pdf-organize")]
    public void Output_folder_equal_to_input_folder_never_touches_the_input(string id)
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "Z");
        var b = PdfKit.Make(dir, "b.pdf", 2, "Y");
        var before = IoFile.ReadAllBytes(a);
        var opts = id == "pdf-protect" ? new[] { ("password", "pw") } : id == "pdf-crop" ? new[] { ("left", "5") } : Array.Empty<(string, string)>();
        var files = id == "pdf-merge" ? new[] { a, b } : new[] { a };
        var outs = id == "pdf-unlock" ? PdfKit.RunOk(id, files, dir) : PdfKit.RunOk(id, files, dir, opts);
        Assert.All(outs, o => Assert.NotEqual(a, o));
        Assert.Equal(before, IoFile.ReadAllBytes(a));
        // run twice: second run must not overwrite the first output
        var outs2 = id == "pdf-unlock" ? PdfKit.RunOk(id, files, dir) : PdfKit.RunOk(id, files, dir, opts);
        Assert.Empty(outs.Intersect(outs2));
    }

    [Fact]
    public void Page_numbers_land_inside_an_offset_crop_box()
    {
        var f = PdfKit.Make(PdfKit.NewDir(), "a.pdf", 1, "Q");
        var c = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { f }, PdfKit.NewDir(), ("left", "100"), ("top", "100"), ("right", "100"), ("bottom", "100")));
        var n = Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { c }, PdfKit.NewDir(), ("position", "bottom-right")));
        var box = PdfKit.CropBoxes(n)[0];
        using var d = PdfKit.Open(n);
        var digit = d.GetPage(1).Letters.Last(l => l.Value == "1");
        // PdfPig reports coordinates relative to the crop box origin.
        var w = box[2] - box[0]; var h = box[3] - box[1];
        Assert.True(digit.GlyphRectangle.Left >= 0 && digit.GlyphRectangle.Right <= w, $"x {digit.GlyphRectangle.Left}");
        Assert.True(digit.GlyphRectangle.Bottom >= 0 && digit.GlyphRectangle.Top <= h, $"y {digit.GlyphRectangle.Bottom}");
        Assert.True(digit.GlyphRectangle.Right > w - 60 && digit.GlyphRectangle.Bottom < 60, "bottom-right of the cropped page");
    }
}
