// pdf-to-powerpoint renders with Windows.Data.Pdf, so these tests exist only in the Windows shape.
#if WINDOWS
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

public class WinToolPptxTests
{
    [Fact]
    public void Pdf_to_powerpoint_is_registered_and_available_on_windows()
    {
        var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find("pdf-to-powerpoint");
        Assert.NotNull(tool);
        Assert.NotNull(tool!.Runner);
        Assert.True(tool.Availability.Available, tool.Availability.Text);
    }

    [Fact]
    public void One_slide_per_page_each_with_one_jpeg_picture_in_the_page_ratio()
    {
        var input = WinToolSupport.Fixture("Q3_Report.pdf");
        int pages;
        double width, height;
        using (var doc = PdfDocument.Open(input))
        {
            pages = doc.NumberOfPages;
            width = doc.GetPage(1).Width;
            height = doc.GetPage(1).Height;
        }

        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "not yet there");
        var (code, stdout, stderr) = WinToolSupport.Cli("--tool", "pdf-to-powerpoint", "--in", input, "--out", outDir);
        Assert.True(code == 0, stderr);

        var pptx = IoPath.Combine(outDir, "Q3_Report.pptx");
        Assert.Contains(pptx, stdout);
        var deck = WinToolSupport.ReadDeck(pptx);
        Assert.Equal(pages, deck.Slides.Count);
        Assert.InRange(deck.Width / (double)deck.Height, width / height - 0.01, width / height + 0.01);
        for (var i = 0; i < pages; i++)
        {
            Assert.Equal(1, deck.Slides[i].PictureCount);
            Assert.True(WinToolSupport.IsJpeg(deck.Slides[i].ImageBytes), "slide " + (i + 1) + " picture is not a JPEG");
            Assert.True(deck.Slides[i].ImageBytes.Length > 2000, "slide " + (i + 1) + " picture is nearly empty");
        }
        Assert.Equal("", WinToolSupport.DeckErrors(pptx));
    }

    [Fact]
    public void Png_option_embeds_png_pictures_at_the_asked_resolution()
    {
        var input = WinToolSupport.Fixture("sample.pdf");
        double width;
        using (var doc = PdfDocument.Open(input))
            width = doc.GetPage(1).Width;
        var outDir = WinToolSupport.NewDir();
        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "pdf-to-powerpoint", "--in", input, "--out", outDir, "--opt", "image=png", "--opt", "dpi=100");
        Assert.True(code == 0, stderr);

        var deck = WinToolSupport.ReadDeck(IoPath.Combine(outDir, "sample.pptx"));
        Assert.All(deck.Slides, s => Assert.True(WinToolSupport.IsPng(s.ImageBytes)));
        var (w, _) = WinToolSupport.PngSize(deck.Slides[0].ImageBytes);
        Assert.InRange(w, width / 72.0 * 100 - 2, width / 72.0 * 100 + 2);
    }

    [Fact]
    public void Second_run_keeps_the_first_runs_deck()
    {
        var input = WinToolSupport.Fixture("sample.pdf");
        var outDir = WinToolSupport.NewDir();
        for (var i = 0; i < 2; i++)
        {
            var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-to-powerpoint", "--in", input, "--out", outDir);
            Assert.True(code == 0, stderr);
        }
        Assert.Equal(2, IoDirectory.GetFiles(outDir, "*.pptx").Length);
    }

    [Theory]
    [InlineData("dpi=10", "dpi")]
    [InlineData("dpi=900", "dpi")]
    [InlineData("image=gif", "image")]
    public void A_bad_option_fails_naming_it_and_writes_nothing(string opt, string named)
    {
        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "none");
        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "pdf-to-powerpoint", "--in", WinToolSupport.Fixture("sample.pdf"), "--out", outDir, "--opt", opt);
        Assert.NotEqual(0, code);
        Assert.Contains(named, stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public void A_file_that_is_not_a_pdf_fails_and_leaves_nothing()
    {
        var dir = WinToolSupport.NewDir();
        var bad = IoPath.Combine(dir, "bad.pdf");
        IoFile.WriteAllText(bad, "this is not a pdf");
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-to-powerpoint", "--in", bad, "--out", outDir);
        Assert.NotEqual(0, code);
        Assert.Contains("cannot open", stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }
}
#endif
