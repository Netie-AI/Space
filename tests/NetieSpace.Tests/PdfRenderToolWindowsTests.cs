// pdf-to-jpg runs on Windows.Data.Pdf, so these tests exist only in the Windows shape.
// On the Linux gate this file compiles to nothing; Windows CI runs it.
#if WINDOWS
using NetieSpace.Apps.Tools;
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

public class PdfRenderToolWindowsTests
{
    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string NewDir()
    {
        var dir = IoPath.Combine(IoPath.GetTempPath(), "netie-render-" + Guid.NewGuid().ToString("N"));
        IoDirectory.CreateDirectory(dir);
        return dir;
    }

    private static (int Width, int Height) JpegSize(string path)
    {
        var b = IoFile.ReadAllBytes(path);
        Assert.True(b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8, "not a JPEG: " + path);
        for (var i = 2; i + 9 < b.Length;)
        {
            Assert.Equal(0xFF, b[i]);
            var marker = b[i + 1];
            var length = (b[i + 2] << 8) | b[i + 3];
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
            i += 2 + length;
        }
        throw new Xunit.Sdk.XunitException("no JPEG frame header in " + path);
    }

    [Fact]
    public void Pdf_to_jpg_is_available_on_windows()
    {
        var tool = ToolRegistry.Default.Find("pdf-to-jpg");
        Assert.NotNull(tool);
        Assert.True(tool!.Availability.Available, tool.Availability.Text);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(150)]
    public void Pdf_to_jpg_writes_one_jpeg_per_page_at_the_asked_size(int dpi)
    {
        var input = Fixture("Q3_Report.pdf");
        int pages;
        double widthPoints, heightPoints;
        using (var doc = PdfDocument.Open(input))
        {
            pages = doc.NumberOfPages;
            var first = doc.GetPage(1);
            widthPoints = first.Width;
            heightPoints = first.Height;
        }

        var outDir = IoPath.Combine(NewDir(), "not yet there");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = ToolCli.RunWith(
            new[] { "--tool", "pdf-to-jpg", "--in", input, "--out", outDir, "--opt", "dpi=" + dpi },
            stdout, stderr, ToolRegistry.Default);
        Assert.True(code == 0, stderr.ToString());

        var files = IoDirectory.GetFiles(outDir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(pages, files.Count);
        Assert.EndsWith("Q3_Report-p001.jpg", files[0]);

        var (w, h) = JpegSize(files[0]);
        Assert.InRange(w, widthPoints / 72.0 * dpi - 2, widthPoints / 72.0 * dpi + 2);
        Assert.InRange(h, heightPoints / 72.0 * dpi - 2, heightPoints / 72.0 * dpi + 2);
    }

    [Fact]
    public void Pdf_to_jpg_second_run_keeps_the_first_runs_files()
    {
        var input = Fixture("sample.pdf");
        var outDir = NewDir();
        string[] Run()
        {
            var err = new StringWriter();
            var code = ToolCli.RunWith(new[] { "--tool", "pdf-to-jpg", "--in", input, "--out", outDir },
                new StringWriter(), err, ToolRegistry.Default);
            Assert.True(code == 0, err.ToString());
            return IoDirectory.GetFiles(outDir, "*.jpg");
        }

        var first = Run();
        var second = Run();
        Assert.Equal(first.Length * 2, second.Length);
    }

    [Theory]
    [InlineData("dpi=10")]
    [InlineData("dpi=5000")]
    public void Pdf_to_jpg_refuses_a_dpi_outside_its_range(string opt)
    {
        var outDir = IoPath.Combine(NewDir(), "none");
        var err = new StringWriter();
        var code = ToolCli.RunWith(
            new[] { "--tool", "pdf-to-jpg", "--in", Fixture("sample.pdf"), "--out", outDir, "--opt", opt },
            new StringWriter(), err, ToolRegistry.Default);
        Assert.NotEqual(0, code);
        Assert.Contains("dpi", err.ToString());
        Assert.False(IoDirectory.Exists(outDir) && IoDirectory.GetFiles(outDir).Length > 0);
    }

    [Fact]
    public void Pdf_to_jpg_on_a_file_that_is_not_a_pdf_fails_and_leaves_nothing()
    {
        var dir = NewDir();
        var bad = IoPath.Combine(dir, "bad.pdf");
        IoFile.WriteAllText(bad, "this is not a pdf");
        var outDir = IoPath.Combine(dir, "out");
        var err = new StringWriter();
        var code = ToolCli.RunWith(new[] { "--tool", "pdf-to-jpg", "--in", bad, "--out", outDir },
            new StringWriter(), err, ToolRegistry.Default);
        Assert.NotEqual(0, code);
        Assert.False(IoDirectory.Exists(outDir) && IoDirectory.GetFiles(outDir).Length > 0);
    }
}
#endif
