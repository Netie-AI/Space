// pdf-redact renders with Windows.Data.Pdf, so these tests exist only in the Windows shape.
#if WINDOWS
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Pdf.WinRt;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

public class WinTool2RedactTests
{
    private const string Secret = "NETIECONFIDENTIAL42";

    private static string MakePdf(string dir, string name, params (string Text, int Rotate)[] pages)
    {
        using var doc = new PdfSharpCore.Pdf.PdfDocument();
        foreach (var (text, rotate) in pages)
        {
            var page = doc.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            page.Rotate = rotate;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(text, PdfToolSupport.ResolveFont(rotate == 0 ? 20 : 40), XBrushes.Black, new XPoint(60, 120));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    private static string ThreePages(string dir) => MakePdf(dir, "report.pdf",
        ("Intro line then " + Secret + " and more words", 0),
        ("This page has only harmless words OTHERPAGETEXT", 0),
        ("closing " + Secret.ToLowerInvariant() + " lower case here", 0));

    /// <summary>True when the term is in the file bytes or in any decoded stream, as ASCII or UTF-16.</summary>
    private static bool Leaks(string pdfPath, string term)
    {
        var lowered = term.ToLowerInvariant();
        var patterns = new List<byte[]>
        {
            System.Text.Encoding.ASCII.GetBytes(lowered),
            System.Text.Encoding.BigEndianUnicode.GetBytes(lowered),
            System.Text.Encoding.Unicode.GetBytes(lowered),
            // <hex> strings, ASCII and UTF-16BE
            System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(lowered)).ToLowerInvariant()),
            System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes(lowered)).ToLowerInvariant()),
        };
        bool Has(byte[] hay)
        {
            var lower = (byte[])hay.Clone();
            for (var i = 0; i < lower.Length; i++)
                if (lower[i] >= 'A' && lower[i] <= 'Z') lower[i] = (byte)(lower[i] + 32);
            return patterns.Any(p => lower.AsSpan().IndexOf(p) >= 0);
        }
        if (Has(IoFile.ReadAllBytes(pdfPath))) return true;
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        foreach (var item in doc.Internals.GetAllObjects())
        {
            if (item is not PdfSharpCore.Pdf.PdfDictionary dict || dict.Stream is null) continue;
            byte[] bytes;
            try { bytes = dict.Stream.UnfilteredValue; }
            catch { continue; }
            if (Has(bytes)) return true;
        }
        return false;
    }

    private static async Task<SixLabors.ImageSharp.Image<Rgba32>> Render(string pdf, int pageIndex, int dpi)
    {
        var doc = await WinPdfRender.OpenAsync(pdf, CancellationToken.None);
        using var page = doc.GetPage((uint)pageIndex);
        var (w, h) = WinPdfRender.PixelSize(page, dpi, pdf, pageIndex + 1);
        using var stream = await WinPdfRender.RenderAsync(
            page, w, h, Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, pageIndex + 1, pdf, CancellationToken.None);
        using var read = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        return SixLabors.ImageSharp.Image.Load<Rgba32>(read);
    }

    private static bool Red(Rgba32 p) => p.R > 230 && p.G < 40 && p.B < 40;
    private static bool Ink(Rgba32 p) => p.R < 200 || p.G < 200 || p.B < 200;

    [Fact]
    public void Pdf_redact_is_registered_and_available_on_windows()
    {
        var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find("pdf-redact");
        Assert.NotNull(tool);
        Assert.NotNull(tool!.Runner);
        Assert.True(tool.Availability.Available, tool.Availability.Text);
        Assert.Contains("pictures", tool.Entry.Notes);
    }

    [Fact]
    public void The_scanner_can_fail_it_finds_a_term_in_bytes_and_in_streams()
    {
        var dir = WinToolSupport.NewDir();
        var plain = IoPath.Combine(dir, "title.pdf");
        using (var doc = new PdfSharpCore.Pdf.PdfDocument())
        {
            doc.AddPage();
            doc.Info.Title = Secret;
            doc.Save(plain);
        }
        Assert.True(Leaks(plain, Secret), "the byte scanner missed a term in the document info");
        Assert.False(Leaks(plain, "ZZNOTINTHEFILEZZ"));
    }

    [Fact]
    public void Text_mode_leaves_no_text_on_the_boxed_pages_and_keeps_the_other_page()
    {
        var dir = WinToolSupport.NewDir();
        var input = ThreePages(dir);
        using (var before = PdfDocument.Open(input))
        {
            Assert.Contains(Secret, before.GetPage(1).Text);
            Assert.Contains(Secret.ToLowerInvariant(), before.GetPage(3).Text);
        }
        var beforeHash = IoFile.ReadAllBytes(input);

        var outDir = IoPath.Combine(dir, "out");
        var (code, stdout, stderr) = WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir, "--opt", "text=NetieConfidential42");
        Assert.True(code == 0, stderr);
        var output = IoPath.Combine(outDir, "report-redacted.pdf");
        Assert.True(IoFile.Exists(output), "expected " + output);
        Assert.Contains(output, stdout);
        Assert.Single(IoDirectory.GetFiles(outDir));
        Assert.Equal(beforeHash, IoFile.ReadAllBytes(input));

        using (var doc = PdfDocument.Open(output))
        {
            Assert.Equal(3, doc.NumberOfPages);
            foreach (var n in new[] { 1, 3 })
            {
                var page = doc.GetPage(n);
                Assert.Empty(page.Letters);
                Assert.Empty(page.GetWords());
                Assert.Equal(1, page.GetImages().Count());
                Assert.InRange(page.Width, 594, 597);
                Assert.InRange(page.Height, 841, 843);
            }
            var other = doc.GetPage(2).Text;
            Assert.Contains("OTHERPAGETEXT", other);
            Assert.Contains("harmless", other);
            Assert.DoesNotContain(Secret, string.Join(" ", Enumerable.Range(1, 3).Select(i => doc.GetPage(i).Text)), StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(Leaks(output, Secret), "the redacted word is still in the file bytes or a decoded stream");
    }

    [Fact]
    public async Task Every_ink_pixel_of_the_word_is_covered_on_pages_turned_0_90_180_and_270()
    {
        var dir = WinToolSupport.NewDir();
        var input = MakePdf(dir, "turned.pdf", ("ROTWORD", 0), ("ROTWORD", 90), ("ROTWORD", 180), ("ROTWORD", 270));
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir,
            "--opt", "text=rotword", "--opt", "color=#FF0000", "--opt", "dpi=100");
        Assert.True(code == 0, stderr);
        var output = IoPath.Combine(outDir, "turned-redacted.pdf");

        for (var i = 0; i < 4; i++)
        {
            using var before = await Render(input, i, 100);
            using var after = await Render(output, i, 100);
            Assert.InRange(Math.Abs(before.Width - after.Width), 0, 2);
            Assert.InRange(Math.Abs(before.Height - after.Height), 0, 2);
            int ink = 0, uncovered = 0, red = 0;
            for (var y = 0; y < Math.Min(before.Height, after.Height); y++)
                for (var x = 0; x < Math.Min(before.Width, after.Width); x++)
                {
                    if (Red(after[x, y])) red++;
                    if (!Ink(before[x, y])) continue;
                    ink++;
                    if (!Red(after[x, y])) uncovered++;
                }
            Assert.True(ink > 200, $"page {i + 1}: the original shows only {ink} ink pixels");
            Assert.True(uncovered == 0, $"page {i + 1}: {uncovered} of {ink} ink pixels are not under a box");
            Assert.True(red < ink * 12, $"page {i + 1}: the box ({red} px) is far larger than the word ({ink} px)");
        }
    }

    [Fact]
    public async Task Areas_are_painted_in_the_chosen_colour_where_asked_and_nowhere_else()
    {
        var dir = WinToolSupport.NewDir();
        var input = ThreePages(dir);
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir,
            "--opt", "regions=1:100,300,200,50;3:0,0,50,50", "--opt", "color=#FF0000", "--opt", "dpi=100");
        Assert.True(code == 0, stderr);
        var output = IoPath.Combine(outDir, "report-redacted.pdf");

        using (var doc = PdfDocument.Open(output))
        {
            Assert.Empty(doc.GetPage(1).Letters);
            Assert.Contains("OTHERPAGETEXT", doc.GetPage(2).Text);
            Assert.Empty(doc.GetPage(3).Letters);
        }
        using (var one = await Render(output, 0, 100))
        {
            Assert.True(Red(one[(int)(200 * 100 / 72.0), (int)(325 * 100 / 72.0)]), "centre of the area on page 1 is not red");
            Assert.False(Red(one[(int)(500 * 100 / 72.0), (int)(600 * 100 / 72.0)]), "a pixel outside the area is red");
        }
        using (var three = await Render(output, 2, 100))
            Assert.True(Red(three[(int)(20 * 100 / 72.0), (int)(20 * 100 / 72.0)]), "the corner area on page 3 is not red");
    }

    [Fact]
    public async Task A_word_of_the_fixture_is_black_at_the_centre_of_where_the_text_reader_found_it()
    {
        var input = WinToolSupport.Fixture("Q3_Report.pdf");
        string word; double cx, cy, pageHeight;
        using (var doc = PdfDocument.Open(input))
        {
            var page = doc.GetPage(1);
            Assert.Equal(0, page.Rotation.Value);
            var hit = page.GetWords().First(w => w.Text.Length >= 7 && w.Text.All(char.IsLetter));
            word = hit.Text;
            cx = hit.BoundingBox.Centroid.X;
            cy = hit.BoundingBox.Centroid.Y;
            pageHeight = page.Height;
        }
        var outDir = WinToolSupport.NewDir();
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir, "--opt", "text=" + word, "--opt", "dpi=100");
        Assert.True(code == 0, stderr);
        var output = IoPath.Combine(outDir, "Q3_Report-redacted.pdf");
        using (var doc = PdfDocument.Open(output))
        {
            Assert.Empty(doc.GetPage(1).Letters);
            Assert.DoesNotContain(word, doc.GetPage(1).Text, StringComparison.OrdinalIgnoreCase);
        }
        using var img = await Render(output, 0, 100);
        var p = img[(int)(cx * 100 / 72.0), (int)((pageHeight - cy) * 100 / 72.0)];
        Assert.True(p.R < 40 && p.G < 40 && p.B < 40, "centre of the word is " + p);
    }

    [Fact]
    public void A_request_that_matches_nothing_fails_naming_the_option_and_writes_nothing()
    {
        var dir = WinToolSupport.NewDir();
        var input = ThreePages(dir);

        void Fails(string optionName, params string[] opts)
        {
            var outDir = IoPath.Combine(dir, "out-" + Guid.NewGuid().ToString("N"));
            var args = new List<string> { "--tool", "pdf-redact", "--in", input, "--out", outDir };
            foreach (var o in opts) { args.Add("--opt"); args.Add(o); }
            var (code, _, stderr) = WinToolSupport.Cli(args.ToArray());
            Assert.NotEqual(0, code);
            Assert.Contains("'" + optionName + "'", stderr);
            Assert.False(WinToolSupport.HasFiles(outDir), string.Join(" ", opts) + " left files behind");
        }

        Fails("text", "text=ZZNOTTHEREZZ");
        Fails("text", "text=" + Secret + ";ZZNOTTHEREZZ");
        Fails("regions", "regions=9:0,0,10,10");
        Fails("regions", "regions=1:5000,5000,10,10");
        Fails("regions", "regions=abc");
        Fails("regions", "regions=1:0,0,0,10");
        Fails("regions' or 'text", "dpi=150");
        Fails("dpi", "text=" + Secret, "dpi=50");
        Fails("color", "text=" + Secret, "color=red");
        Fails("color", "text=" + Secret, "color=#80FF0000");
    }

    [Fact]
    public void A_file_that_is_not_a_pdf_fails_and_a_second_run_keeps_the_first_output()
    {
        var dir = WinToolSupport.NewDir();
        var bad = IoPath.Combine(dir, "bad.pdf");
        IoFile.WriteAllText(bad, "this is not a pdf");
        var outBad = IoPath.Combine(dir, "bad-out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-redact", "--in", bad, "--out", outBad, "--opt", "text=x");
        Assert.NotEqual(0, code);
        Assert.Contains("cannot open", stderr);
        Assert.False(WinToolSupport.HasFiles(outBad));

        var input = ThreePages(dir);
        var outDir = IoPath.Combine(dir, "out");
        Assert.Equal(0, WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir, "--opt", "text=" + Secret).Code);
        Assert.Equal(0, WinToolSupport.Cli("--tool", "pdf-redact", "--in", input, "--out", outDir, "--opt", "text=" + Secret).Code);
        Assert.Equal(2, IoDirectory.GetFiles(outDir, "*.pdf").Length);
        Assert.Equal(2, IoDirectory.GetFiles(outDir).Length);
    }
}
#endif
