using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Tests;

public class PdfTool3CompressTests
{
    private static long Len(string p) => new FileInfo(p).Length;

    private static double MeanLuma(Image<Rgb24> img)
    {
        double s = 0; long n = 0;
        for (var y = 0; y < img.Height; y += 3)
            for (var x = 0; x < img.Width; x += 3) { var p = img[x, y]; s += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B; n++; }
        return s / n;
    }

    [Fact]
    public void A_large_photo_shrinks_a_lot_and_the_text_and_pages_survive()
    {
        var dir = PdfKit.NewDir();
        var photo = PdfTool3Kit.PhotoPng(dir, "photo.png", 1600, 1200);
        var pdf = PdfTool3Kit.PdfWithImages(dir, "scan report.pdf", "Quarterly photo sheet", (photo, 400, 300));

        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir()));

        Assert.True(Len(outPath) < Len(pdf) / 3, $"expected a real saving, got {Len(pdf)} -> {Len(outPath)}");
        Assert.Equal(PdfKit.PageCount(pdf), PdfKit.PageCount(outPath));
        Assert.Equal(PdfKit.Texts(pdf), PdfKit.Texts(outPath));
        Assert.Equal("Quarterly photo sheet", PdfKit.Texts(outPath)[0]);

        // The picture a person sees: a JPEG at or below the level's resolution, same look as the original.
        using var d = PdfKit.Open(outPath);
        var im = Assert.Single(d.GetPage(1).GetImages());
        var ppi = im.WidthInSamples / (im.Bounds.Width / 72.0);
        Assert.InRange(ppi, 100, 155);
        var bytes = im.RawBytes.ToArray();
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        using var after = Image.Load<Rgb24>(bytes);
        using var before = Image.Load<Rgb24>(photo);
        Assert.InRange(MeanLuma(after), MeanLuma(before) - 4, MeanLuma(before) + 4);
        Assert.Equal(1200.0 / 1600.0, (double)after.Height / after.Width, 2);
    }

    [Fact]
    public void Levels_order_by_size_and_a_jpeg_source_is_recompressed_too()
    {
        var dir = PdfKit.NewDir();
        var jpg = PdfTool3Kit.PhotoJpeg(dir, "photo.jpg", 1800, 1200);
        var pdf = PdfTool3Kit.PdfWithImages(dir, "j.pdf", "JPEG source", (jpg, 360, 240));

        var low = Len(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir(), ("level", "low"))[0]);
        var rec = Len(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir(), ("level", "recommended"))[0]);
        var ext = Len(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir(), ("level", "extreme"))[0]);

        Assert.True(ext < rec && rec < low && low < Len(pdf), $"{Len(pdf)} > {low} > {rec} > {ext} expected");
    }

    [Fact]
    public void A_text_only_pdf_that_cannot_get_smaller_is_refused_and_nothing_is_written()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.TextOnly(dir, "plain.pdf");
        var outDir = PdfKit.NewDir();

        var r = PdfKit.Run("pdf-compress", new[] { pdf }, outDir);

        Assert.False(r.Ok);
        Assert.Contains("already compact", r.Error);
        Assert.Contains("plain.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public void Uncompressed_streams_are_deflated_and_the_text_is_identical()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.TextHeavyUncompressed(dir, "heavy.pdf", 3, 60);

        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir()));

        Assert.True(Len(outPath) < Len(pdf) * 0.6, $"{Len(pdf)} -> {Len(outPath)}");
        Assert.Equal(3, PdfKit.PageCount(outPath));
        Assert.Equal(PdfKit.Texts(pdf), PdfKit.Texts(outPath));
    }

    [Fact]
    public void An_image_with_transparency_is_left_exactly_as_it_was()
    {
        var dir = PdfKit.NewDir();
        var opaque = PdfTool3Kit.PhotoPng(dir, "opaque.png", 1600, 1200, 3);
        var clear = PdfTool3Kit.PhotoPng(dir, "clear.png", 1000, 800, 4, alpha: true);
        var pdf = PdfTool3Kit.PdfWithImages(dir, "mixed.pdf", "mask test", (opaque, 400, 300), (clear, 300, 240));
        var before = PdfTool3Kit.ImageObjects(pdf);
        var maskedBefore = Assert.Single(before, o => o.Dict.Elements.ContainsKey("/SMask"));

        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir()));

        var after = PdfTool3Kit.ImageObjects(outPath);
        var maskedAfter = Assert.Single(after, o => o.Dict.Elements.ContainsKey("/SMask"));
        Assert.Equal(maskedBefore.Raw, maskedAfter.Raw);
        Assert.Equal(maskedBefore.Dict.Elements.GetInteger("/Width"), maskedAfter.Dict.Elements.GetInteger("/Width"));
        var maskBefore = before.Single(o => o.Id == ((PdfSharpCore.Pdf.Advanced.PdfReference)maskedBefore.Dict.Elements["/SMask"]).ObjectID.ObjectNumber).Raw;
        var maskAfter = after.Single(o => o.Id == ((PdfSharpCore.Pdf.Advanced.PdfReference)maskedAfter.Dict.Elements["/SMask"]).ObjectID.ObjectNumber).Raw;
        Assert.Equal(maskBefore, maskAfter);
        // The opaque photo next to it did shrink: downsampled and a fraction of its old bytes.
        var photoBefore = before.Single(o => o.Dict.Elements.GetInteger("/Width") == 1600);
        // Every 1000 px object (the picture, its soft mask, its stencil) is byte for byte what it was.
        var keptBefore = before.Where(o => o.Dict.Elements.GetInteger("/Width") == 1000).Select(o => Convert.ToHexString(o.Raw)).OrderBy(x => x).ToList();
        var keptAfter = after.Where(o => o.Dict.Elements.GetInteger("/Width") == 1000).Select(o => Convert.ToHexString(o.Raw)).OrderBy(x => x).ToList();
        Assert.Equal(3, keptBefore.Count);
        Assert.Equal(keptBefore, keptAfter);
        var photoAfter = Assert.Single(after, o => o.Dict.Elements.GetInteger("/Width") < 1000);
        Assert.True(photoAfter.Raw.Length < photoBefore.Raw.Length / 4);
    }

    [Fact]
    public void An_image_drawn_below_the_threshold_density_keeps_its_pixels()
    {
        var dir = PdfKit.NewDir();
        var small = PdfTool3Kit.PhotoPng(dir, "small.png", 400, 300);
        var pdf = PdfTool3Kit.PdfWithImages(dir, "low-density.pdf", "small picture", (small, 400, 300)); // 72 ppi
        var before = Assert.Single(PdfTool3Kit.ImageObjects(pdf));

        var r = PdfKit.Run("pdf-compress", new[] { pdf }, PdfKit.NewDir());

        if (r.Ok)
        {
            var after = Assert.Single(PdfTool3Kit.ImageObjects(r.Outputs[0]));
            Assert.Equal(400, after.Dict.Elements.GetInteger("/Width"));
            Assert.Equal(300, after.Dict.Elements.GetInteger("/Height"));
            Assert.Equal(before.Dict.Elements["/Filter"].ToString(), after.Dict.Elements["/Filter"].ToString());
        }
        else Assert.Contains("already compact", r.Error);
    }

    [Fact]
    public void Identical_pictures_are_stored_once()
    {
        var dir = PdfKit.NewDir();
        var photo = PdfTool3Kit.PhotoPng(dir, "same.png", 600, 450);
        using var doc = new PdfDocument();
        for (var i = 0; i < 2; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            using var xi = XImage.FromFile(photo);
            g.DrawImage(xi, 40, 60, 600, 450); // 72 ppi: below every threshold, so only the merge can save bytes
        }
        var pdf = IoPath.Combine(dir, "twice.pdf");
        doc.Save(pdf);
        Assert.Equal(2, PdfTool3Kit.ImageObjects(pdf).Count);

        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir(), ("level", "low")));

        Assert.Single(PdfTool3Kit.ImageObjects(outPath));
        Assert.Equal(2, PdfKit.PageCount(outPath));
        using var d = PdfKit.Open(outPath);
        Assert.Single(d.GetPage(1).GetImages());
        Assert.Single(d.GetPage(2).GetImages());
    }

    [Fact]
    public void An_object_nothing_points_at_is_not_carried_over()
    {
        var dir = PdfKit.NewDir();
        var blob = new byte[200_000];
        new Random(9).NextBytes(blob);
        // Hand-written file: object 6 is a 200 KB stream that no other object mentions.
        var content = "BT /F1 14 Tf 40 700 Td (orphan test) Tj ET";
        var bodies = new List<byte[]>();
        byte[] Ascii(string t) => System.Text.Encoding.ASCII.GetBytes(t);
        bodies.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        bodies.Add(Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));
        bodies.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"));
        bodies.Add(Ascii($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        bodies.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        var head = Ascii($"<< /Length {blob.Length} >>\nstream\n");
        bodies.Add(head.Concat(blob).Concat(Ascii("\nendstream")).ToArray());
        using var ms = new MemoryStream();
        void W(byte[] b) => ms.Write(b, 0, b.Length);
        W(Ascii("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Position);
            W(Ascii($"{i + 1} 0 obj\n")); W(bodies[i]); W(Ascii("\nendobj\n"));
        }
        var xref = ms.Position;
        W(Ascii($"xref\n0 {bodies.Count + 1}\n0000000000 65535 f \n"));
        foreach (var o in offsets) W(Ascii($"{o:D10} 00000 n \n"));
        W(Ascii($"trailer\n<< /Size {bodies.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        var pdf = IoPath.Combine(dir, "orphan.pdf");
        IoFile.WriteAllBytes(pdf, ms.ToArray());
        Assert.Equal(new[] { "orphan test" }, PdfKit.Texts(pdf));
        Assert.True(Len(pdf) > 200_000, "fixture must really carry the orphan");

        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { pdf }, PdfKit.NewDir()));

        Assert.True(Len(outPath) < 20_000, $"{Len(pdf)} -> {Len(outPath)}");
        Assert.Equal(new[] { "orphan test" }, PdfKit.Texts(outPath));
    }

    [Fact]
    public void Same_named_inputs_get_distinct_outputs_and_the_input_is_never_overwritten()
    {
        var a = PdfKit.NewDir();
        var b = PdfKit.NewDir();
        var pa = PdfTool3Kit.PdfWithImages(a, "report.pdf", "A", (PdfTool3Kit.PhotoPng(a, "p.png", 1400, 1000, 5), 400, 280));
        var pb = PdfTool3Kit.PdfWithImages(b, "report.pdf", "B", (PdfTool3Kit.PhotoPng(b, "p.png", 1400, 1000, 6), 400, 280));
        var bytesA = IoFile.ReadAllBytes(pa);
        var outDir = PdfKit.NewDir();

        var outs = PdfKit.RunOk("pdf-compress", new[] { pa, pb }, outDir);

        Assert.Equal(2, outs.Count);
        Assert.NotEqual(outs[0], outs[1]);
        Assert.All(outs, o => Assert.Equal(outDir, IoPath.GetDirectoryName(o)));
        Assert.Equal("A", PdfKit.Texts(outs[0])[0]);
        Assert.Equal("B", PdfKit.Texts(outs[1])[0]);
        Assert.Equal(bytesA, IoFile.ReadAllBytes(pa));
    }

    [Fact]
    public void A_batch_with_one_incompressible_file_writes_nothing_and_names_it()
    {
        var dir = PdfKit.NewDir();
        var good = PdfTool3Kit.PdfWithImages(dir, "good.pdf", "ok", (PdfTool3Kit.PhotoPng(dir, "g.png", 1400, 1000), 400, 280));
        var bad = PdfTool3Kit.TextOnly(dir, "bad.pdf");
        var outDir = PdfKit.NewDir();

        var r = PdfKit.Run("pdf-compress", new[] { good, bad }, outDir);

        Assert.False(r.Ok);
        Assert.Contains("bad.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public void Bad_options_and_bad_inputs_fail_with_a_message_that_names_the_problem()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.TextOnly(dir, "x.pdf");
        var r1 = PdfKit.Run("pdf-compress", new[] { pdf }, PdfKit.NewDir(), ("level", "tiny"));
        Assert.False(r1.Ok);
        Assert.Contains("level", r1.Error);

        var junk = IoPath.Combine(dir, "junk.pdf");
        IoFile.WriteAllText(junk, "this is not a pdf");
        var r2 = PdfKit.Run("pdf-compress", new[] { junk }, PdfKit.NewDir());
        Assert.False(r2.Ok);
        Assert.Contains("junk.pdf", r2.Error);
    }

    [Fact]
    public async Task A_cancelled_run_reports_cancelled_and_writes_nothing()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.PdfWithImages(dir, "c.pdf", "c", (PdfTool3Kit.PhotoPng(dir, "c.png", 1400, 1000), 400, 280));
        var outDir = PdfKit.NewDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tool = PdfKit.Registry.Find("pdf-compress")!;
        var r = await ToolRunner.RunAsync(tool, new ToolRequest(new[] { pdf }, outDir, new Dictionary<string, string>()), null, cts.Token);
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }
}
