using SixLabors.ImageSharp;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Tests;

/// <summary>Audit of the PDF runners: fidelity of what a person gets back (title, links, sizes, rotation).</summary>
public class AuditPdfTests
{
    /// <summary>Three pages of different shape: letter, a small page shown rotated 90, an A4 landscape. Title and a link on every page.</summary>
    internal static string Rich(string dir, string name = "rich.pdf", string title = "Quarterly Plan")
    {
        using var doc = new PdfSharpCore.Pdf.PdfDocument();
        doc.Info.Title = title;
        doc.Info.Author = "Audit";
        var shapes = new[] { (612.0, 792.0, 0), (300.0, 400.0, 90), (842.0, 595.0, 0) };
        for (var i = 0; i < shapes.Length; i++)
        {
            var page = doc.AddPage();
            page.Width = shapes[i].Item1; page.Height = shapes[i].Item2;
            if (shapes[i].Item3 != 0) page.Rotate = shapes[i].Item3;
            using (var g = XGraphics.FromPdfPage(page))
                g.DrawString($"RICH page {i + 1}", new XFont("Arial", 14), XBrushes.Black, new XPoint(40, 60));
            page.AddWebLink(new PdfRectangle(new XRect(30, 30, 120, 40)), "https://example.com/p" + (i + 1));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    private static List<(double W, double H, int Rot)> Shapes(string path)
    {
        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, d.PageCount).Select(i => (Math.Round(d.Pages[i].MediaBox.Width, 1), Math.Round(d.Pages[i].MediaBox.Height, 1), d.Pages[i].Rotate)).ToList();
    }

    private static string? TitleOf(string path, string? pw = null)
    {
        using var d = PdfKit.Open(path, pw);
        return d.Information.Title;
    }

    private static List<string> LinksOf(string path)
    {
        using var d = PdfKit.Open(path);
        return d.GetPages().SelectMany(p => p.GetHyperlinks()).Select(h => h.Uri ?? "").ToList();
    }

    private static readonly (string Id, (string, string)[] Opts)[] SingleInputTools =
    {
        ("pdf-extract-pages", new[] { ("pages", "1-end") }),
        ("pdf-remove-pages", new[] { ("pages", "3") }),
        ("pdf-split", new[] { ("split_mode", "ranges"), ("ranges", "1-2") }),
        ("pdf-organize", Array.Empty<(string, string)>()),
        ("pdf-rotate", new[] { ("direction", "right") }),
        ("pdf-watermark", new[] { ("text", "DRAFT") }),
        ("pdf-page-numbers", Array.Empty<(string, string)>()),
        ("pdf-crop", new[] { ("left", "10") }),
    };

    [Fact]
    public void Document_title_survives_every_tool_that_does_not_mean_to_change_it()
    {
        var dir = PdfKit.NewDir();
        var input = Rich(dir);
        var bad = new List<string>();
        foreach (var (id, opts) in SingleInputTools)
        {
            var outs = PdfKit.Run(id, new[] { input }, PdfKit.NewDir(), opts);
            Assert.True(outs.Ok, id + ": " + outs.Error);
            foreach (var f in outs.Outputs)
                if (TitleOf(f) != "Quarterly Plan") bad.Add($"{id}: title is '{TitleOf(f)}'");
        }
        var second = Rich(dir, "second.pdf", "Other");
        var m = PdfKit.RunOk("pdf-merge", new[] { input, second }, PdfKit.NewDir());
        if (TitleOf(m[0]) != "Quarterly Plan") bad.Add($"pdf-merge: title is '{TitleOf(m[0])}'");
        var p = PdfKit.RunOk("pdf-protect", new[] { input }, PdfKit.NewDir(), ("password", "pw1"));
        if (TitleOf(p[0], "pw1") != "Quarterly Plan") bad.Add($"pdf-protect: title is '{TitleOf(p[0], "pw1")}'");
        using (var protectedDoc = PdfKit.Open(p[0], "pw1"))
            if (protectedDoc.Information.Author != "Audit") bad.Add($"pdf-protect: author is '{protectedDoc.Information.Author}'");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Links_survive_every_tool_that_keeps_the_page()
    {
        var dir = PdfKit.NewDir();
        var input = Rich(dir);
        var bad = new List<string>();
        foreach (var (id, opts) in SingleInputTools.Where(t => t.Id != "pdf-remove-pages" && t.Id != "pdf-split"))
        {
            var outs = PdfKit.RunOk(id, new[] { input }, PdfKit.NewDir(), opts);
            var links = LinksOf(outs[0]);
            if (links.Count != 3) bad.Add($"{id}: {links.Count} links left of 3");
        }
        var rm = PdfKit.RunOk("pdf-remove-pages", new[] { input }, PdfKit.NewDir(), ("pages", "3"));
        if (LinksOf(rm[0]).Count != 2) bad.Add("pdf-remove-pages: links of the kept pages are gone");
        var sp = PdfKit.RunOk("pdf-split", new[] { input }, PdfKit.NewDir(), ("split_mode", "ranges"), ("ranges", "1-2"));
        if (LinksOf(sp[0]).Count != 2) bad.Add("pdf-split: links of the kept pages are gone");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Mixed_page_sizes_and_a_rotated_page_come_through_page_tools_unchanged()
    {
        var dir = PdfKit.NewDir();
        var input = Rich(dir);
        var want = Shapes(input);
        Assert.Equal(new[] { 0, 90, 0 }, want.Select(s => s.Rot).ToArray());
        var bad = new List<string>();
        foreach (var (id, opts) in SingleInputTools.Where(t => t.Id is "pdf-extract-pages" or "pdf-organize" or "pdf-watermark" or "pdf-page-numbers"))
        {
            var got = Shapes(PdfKit.RunOk(id, new[] { input }, PdfKit.NewDir(), opts)[0]);
            if (!got.SequenceEqual(want)) bad.Add($"{id}: {string.Join(" ", got)} instead of {string.Join(" ", want)}");
        }
        var fixedParts = PdfKit.RunOk("pdf-split", new[] { input }, PdfKit.NewDir(), ("split_mode", "fixed_range"), ("every", "1"));
        for (var i = 0; i < 3; i++)
            if (!Shapes(fixedParts[i]).Single().Equals(want[i])) bad.Add($"pdf-split part {i + 1}: {Shapes(fixedParts[i]).Single()} instead of {want[i]}");
        var merged = Shapes(PdfKit.RunOk("pdf-merge", new[] { input, input }, PdfKit.NewDir())[0]);
        if (!merged.SequenceEqual(want.Concat(want))) bad.Add("pdf-merge changed page shapes");
        var rotated = Shapes(PdfKit.RunOk("pdf-rotate", new[] { input }, PdfKit.NewDir(), ("direction", "right"))[0]);
        if (!rotated.Select(s => s.Rot).SequenceEqual(new[] { 90, 180, 90 })) bad.Add("pdf-rotate: rotations " + string.Join(",", rotated.Select(s => s.Rot)));
        if (!rotated.Select(s => (s.W, s.H)).SequenceEqual(want.Select(s => (s.W, s.H)))) bad.Add("pdf-rotate changed a media box");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void An_encrypted_pdf_given_to_any_tool_but_unlock_fails_with_a_message_and_leaves_no_file()
    {
        var dir = PdfKit.NewDir();
        var plain = Rich(dir);
        var locked = PdfKit.RunOk("pdf-protect", new[] { plain }, PdfKit.NewDir(), ("password", "secret"))[0];
        var cases = new List<(string Id, string[] Files, (string, string)[] Opts)>
        {
            ("pdf-merge", new[] { locked, plain }, Array.Empty<(string, string)>()),
            ("pdf-split", new[] { locked }, new[] { ("split_mode", "fixed_range"), ("every", "1") }),
            ("pdf-remove-pages", new[] { locked }, new[] { ("pages", "1") }),
            ("pdf-extract-pages", new[] { locked }, new[] { ("pages", "1") }),
            ("pdf-organize", new[] { locked }, Array.Empty<(string, string)>()),
            ("pdf-rotate", new[] { locked }, Array.Empty<(string, string)>()),
            ("pdf-page-numbers", new[] { locked }, Array.Empty<(string, string)>()),
            ("pdf-watermark", new[] { locked }, Array.Empty<(string, string)>()),
            ("pdf-crop", new[] { locked }, new[] { ("left", "5") }),
            ("pdf-protect", new[] { locked }, new[] { ("password", "x") }),
            ("pdf-compare", new[] { locked, plain }, Array.Empty<(string, string)>()),
            ("pdf-to-markdown", new[] { locked }, Array.Empty<(string, string)>()),
            ("pdf-extract-text", new[] { locked }, Array.Empty<(string, string)>()),
        };
        var bad = new List<string>();
        foreach (var (id, files, opts) in cases)
        {
            var o = PdfKit.NewDir();
            var r = PdfKit.Run(id, files, o, opts);
            if (r.Ok) { bad.Add($"{id}: produced a file from an encrypted PDF without the password"); continue; }
            if (string.IsNullOrWhiteSpace(r.Error)) bad.Add($"{id}: failed with no message");
            if (IoDirectory.GetFileSystemEntries(o).Length != 0) bad.Add($"{id}: left files behind after failing");
            if (r.Error!.Contains("NullReference") || r.Error.Contains("IndexOutOfRange")) bad.Add($"{id}: crashed: {r.Error}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Page_ranges_resolve_to_the_pages_a_person_meant()
    {
        var dir = PdfKit.NewDir();
        var five = PdfKit.Make(dir, "five.pdf", 5);
        List<string> Pick(string spec)
        {
            var r = PdfKit.Run("pdf-extract-pages", new[] { five }, PdfKit.NewDir(), ("pages", spec));
            Assert.True(r.Ok, spec + ": " + r.Error);
            return PdfKit.Texts(r.Outputs[0]).Select(t => t.Replace("DOC page ", "")).ToList();
        }
        Assert.Equal(new[] { "5" }, Pick("end"));
        Assert.Equal(new[] { "4", "5" }, Pick("4-end"));
        Assert.Equal(new[] { "5" }, Pick("-1"));
        Assert.Equal(new[] { "4" }, Pick("-2"));
        Assert.Equal(new[] { "1", "2", "3", "2", "3", "4" }, Pick("1-3,2-4"));
        Assert.Equal(new[] { "3", "1" }, Pick("3,1"));
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, Pick("1-end"));

        foreach (var bad in new[] { "9", "0", "5-2", "6-7", "1-9", "abc", "1--3" })
        {
            var o = PdfKit.NewDir();
            var r = PdfKit.Run("pdf-extract-pages", new[] { five }, o, ("pages", bad));
            Assert.False(r.Ok, "pages=" + bad + " should be refused");
            Assert.Contains("pages", r.Error!);
            Assert.Empty(IoDirectory.GetFileSystemEntries(o));
        }
    }

    [Fact]
    public void Remove_pages_with_overlapping_ranges_removes_each_page_once()
    {
        var five = PdfKit.Make(PdfKit.NewDir(), "five.pdf", 5);
        var r = PdfKit.RunOk("pdf-remove-pages", new[] { five }, PdfKit.NewDir(), ("pages", "1-3,2-4"));
        Assert.Equal(new[] { "DOC page 5" }, PdfKit.Texts(r[0]).ToArray());
    }

    [Theory]
    [InlineData(0, "right", 90)]
    [InlineData(90, "right", 180)]
    [InlineData(180, "right", 270)]
    [InlineData(270, "right", 0)]
    [InlineData(0, "left", 270)]
    [InlineData(90, "left", 0)]
    [InlineData(180, "left", 90)]
    [InlineData(270, "left", 180)]
    public void Rotating_a_page_that_is_already_turned_keeps_its_page_box(int start, string direction, int expected)
    {
        var dir = PdfKit.NewDir();
        var input = PdfKit.Make(dir, "scan.pdf", 1, "SCAN", 300, 400, (page, _) => { if (start != 0) page.Rotate = start; });
        var r = PdfKit.RunOk("pdf-rotate", new[] { input }, PdfKit.NewDir(), ("direction", direction));
        var b = Shapes(input).Single();
        var a = Shapes(r[0]).Single();
        Assert.Equal(start, b.Rot);
        Assert.Equal(expected, a.Rot);
        // The media box is what the file says it is: turning the page must not change it.
        Assert.Equal((b.W, b.H), (a.W, a.H));
        Assert.Equal((300.0, 400.0), (a.W, a.H));
        Assert.Equal("SCAN page 1", PdfKit.Texts(r[0]).Single());
    }

    private static readonly (string Name, int Col, int Row)[] Grid =
    {
        ("top-left", 0, 0), ("top-center", 1, 0), ("top-right", 2, 0),
        ("middle-left", 0, 1), ("middle-center", 1, 1), ("middle-right", 2, 1),
        ("bottom-left", 0, 2), ("bottom-center", 1, 2), ("bottom-right", 2, 2),
    };

    private static (double X, double Y)? Centre(string pdf, string word)
    {
        using var d = PdfKit.Open(pdf);
        var page = d.GetPage(1);
        var w = page.GetWords().FirstOrDefault(x => x.Text == word);
        if (w is null) return null;
        // PdfPig counts y upward from the bottom of a 792 pt page; turn it into rows counted from the top
        return ((w.BoundingBox.Left + w.BoundingBox.Right) / 2, 792 - (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2);
    }

    [Fact]
    public void A_watermark_lands_in_the_cell_it_was_asked_for_at_all_nine_positions()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "p.pdf", 1, "BASE", 612, 792);
        var bad = new List<string>();
        foreach (var (name, col, row) in Grid)
        {
            var o = PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "STAMP"), ("position", name), ("rotation", "0"), ("size", "30"))[0];
            var c = Centre(o, "STAMP");
            if (c is null) { bad.Add(name + ": no stamp text found"); continue; }
            if ((int)(c.Value.X / (612 / 3.0)) != col || (int)(c.Value.Y / (792 / 3.0)) != row)
                bad.Add($"{name}: stamp centre at {c.Value.X:0},{c.Value.Y:0} is not in column {col} row {row}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Page_numbers_land_at_the_six_corner_and_edge_positions_and_count_from_start()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "p.pdf", 3, "BASE", 612, 792);
        var bad = new List<string>();
        foreach (var (name, col, row) in Grid.Where(g => g.Row != 1))
        {
            var o = PdfKit.RunOk("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("position", name), ("start", "7"), ("format", "page-n"))[0];
            using var d = PdfKit.Open(o);
            var page = d.GetPage(2);
            var w = page.GetWords().FirstOrDefault(x => x.Text == "8");
            if (w is null) { bad.Add(name + ": page 2 has no number 8: " + page.Text); continue; }
            var cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2;
            var cy = 792 - (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2; // the reader's own page height is not reliable
            if ((int)(cx / (612 / 3.0)) != col || (int)(cy / (792 / 3.0)) != row)
                bad.Add($"{name}: number at {cx:0},{cy:0} is not in column {col} row {row}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Numbering_a_chosen_range_counts_only_those_pages()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "p.pdf", 5, "BASE");
        var o = PdfKit.RunOk("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("pages", "2-4"), ("format", "n-of-total"))[0];
        var texts = PdfKit.Texts(o);
        Assert.DoesNotContain(" of ", texts[0]);
        Assert.Contains("1 of 3", texts[1]);
        Assert.Contains("3 of 3", texts[3]);
        Assert.DoesNotContain(" of ", texts[4]);
    }

    [Fact]
    public void A_transparent_png_keeps_its_transparency_on_the_pdf_page_and_an_exif_turn_is_applied()
    {
        var dir = PdfKit.NewDir();
        var png = IoPath.Combine(dir, "logo.png");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(20, 20, new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 0)))
        {
            img[10, 10] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0, 255);
            img.Save(png, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        }
        var o = PdfKit.RunOk("pdf-from-jpg", new[] { png }, PdfKit.NewDir())[0];
        // The engine keeps transparency as a 1-bit stencil mask (or a soft mask): either way the clear pixels are not painted.
        var raw = PdfKit.Latin1(o);
        Assert.True(raw.Contains("/Mask ") || raw.Contains("/SMask"), "the page image has no mask, so the transparent area would be painted");

        var photo = IoPath.Combine(dir, "photo.jpg");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40, 20, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 255)))
        {
            img.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
            img.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)6);
            img.Save(photo, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
        }
        var p = PdfKit.RunOk("pdf-from-jpg", new[] { photo }, PdfKit.NewDir())[0];
        var shape = Shapes(p).Single();
        Assert.True(shape.H > shape.W, "a photo shown upright 20x40 must give a portrait page, got " + shape.W + "x" + shape.H);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3000, 2)]
    [InlineData(20000, 10)]
    public void Pictures_of_extreme_size_make_a_page_a_viewer_can_open(int w, int h)
    {
        var dir = PdfKit.NewDir();
        var png = IoPath.Combine(dir, "x.png");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(w, h, new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 120, 220, 255)))
            img.Save(png, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var o = PdfKit.RunOk("pdf-from-jpg", new[] { png }, PdfKit.NewDir())[0];
        var shape = Shapes(o).Single();
        Assert.InRange(shape.W, 3, 14400);
        Assert.InRange(shape.H, 3, 14400);
        Assert.Equal(w / (double)h, shape.W / shape.H, 2);
    }

    [Fact]
    public void A_picture_too_thin_for_any_page_is_refused_with_a_way_out_and_leaves_nothing()
    {
        var dir = PdfKit.NewDir();
        var png = IoPath.Combine(dir, "thin.png");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 20000, new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 120, 220, 255)))
            img.Save(png, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var o = PdfKit.NewDir();
        var r = PdfKit.Run("pdf-from-jpg", new[] { png }, o);
        Assert.False(r.Ok);
        Assert.Contains("A4", r.Error!);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o));
        var onA4 = PdfKit.RunOk("pdf-from-jpg", new[] { png }, PdfKit.NewDir(), ("pagesize", "A4"));
        Assert.Single(onA4);
    }
}
