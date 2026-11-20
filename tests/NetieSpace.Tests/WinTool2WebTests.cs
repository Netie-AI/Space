// pdf-from-html and img-html-to-image run on WebView2, so these tests exist only in the Windows shape.
// A PC without the WebView2 runtime must FAIL them, with the reason.
#if WINDOWS
using NetieSpace.Apps.Pdf.WinRt;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

public class WinTool2WebTests
{
    private const string Marker = "NetieHtmlProof";

    private static string Html(string dir, string name = "proof.html", string extra = "", int tall = 0)
    {
        var path = IoPath.Combine(dir, name);
        IoFile.WriteAllText(path,
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>t</title>" + extra + "</head>"
            + "<body style=\"margin:0;font:32px Arial\"><div>" + Marker + " 42</div>"
            + "<div style=\"position:absolute;left:100px;top:200px;width:300px;height:100px;background:#ff0000\"></div>"
            + (tall > 0 ? "<div style=\"position:absolute;left:0;top:" + (tall - 50) + "px;width:200px;height:50px;background:#0000ff\"></div>"
                          + "<div style=\"height:" + tall + "px\"></div>" : "")
            + "</body></html>");
        return path;
    }

    private static void RuntimeIsThere()
    {
        string? v = null;
        try { v = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
        Assert.True(!string.IsNullOrWhiteSpace(v), "the WebView2 runtime is not installed on this machine, so the HTML tools cannot run");
    }

    [Fact]
    public void Html_tools_are_registered_and_available_on_windows()
    {
        RuntimeIsThere();
        foreach (var id in new[] { "pdf-from-html", "img-html-to-image" })
        {
            var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find(id);
            Assert.NotNull(tool);
            Assert.NotNull(tool!.Runner);
            Assert.True(tool.Availability.Available, id + ": " + tool.Availability.Text);
        }
    }

    [Fact]
    public void Html_to_pdf_is_one_page_with_the_text_and_leaves_no_profile_folder()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var html = Html(dir);
        var outDir = IoPath.Combine(dir, "out");
        var (code, stdout, stderr) = WinToolSupport.Cli("--tool", "pdf-from-html", "--in", html, "--out", outDir);
        Assert.True(code == 0, stderr);
        var pdf = IoPath.Combine(outDir, "proof.pdf");
        Assert.True(IoFile.Exists(pdf), "expected " + pdf);
        Assert.Contains(pdf, stdout);
        Assert.Single(IoDirectory.GetFiles(outDir));

        using (var doc = PdfDocument.Open(pdf))
        {
            Assert.Equal(1, doc.NumberOfPages);
            var page = doc.GetPage(1);
            Assert.Contains(Marker, page.Text);
            Assert.InRange(page.Width, 590, 600);
            Assert.InRange(page.Height, 838, 846);
        }
        Assert.NotNull(WebView2Host.LastUserDataFolder);
        Assert.False(IoDirectory.Exists(WebView2Host.LastUserDataFolder!), "the temporary WebView2 folder was left behind");
    }

    [Fact]
    public void Landscape_letter_with_no_margin_gives_a_wide_page()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-from-html", "--in", Html(dir), "--out", outDir,
            "--opt", "size=letter", "--opt", "orientation=landscape", "--opt", "margin=0");
        Assert.True(code == 0, stderr);
        using var doc = PdfDocument.Open(IoPath.Combine(outDir, "proof.pdf"));
        var page = doc.GetPage(1);
        Assert.InRange(page.Width, 788, 796);
        Assert.InRange(page.Height, 609, 615);
    }

    [Fact]
    public void A_page_that_reaches_for_the_internet_still_renders_and_the_requests_are_counted()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var html = Html(dir, extra:
            "<link rel=\"stylesheet\" href=\"http://example.invalid/site.css\">"
            + "<script src=\"http://example.invalid/app.js\"></script>"
            + "<img src=\"http://example.invalid/logo.png\">");

        var pdfDir = IoPath.Combine(dir, "pdf");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-from-html", "--in", html, "--out", pdfDir);
        Assert.True(code == 0, stderr);
        Assert.True(WebView2Host.LastBlockedCount >= 1, "no request was counted as blocked: " + WebView2Host.LastBlockedCount);
        using (var doc = PdfDocument.Open(IoPath.Combine(pdfDir, "proof.pdf")))
            Assert.Contains(Marker, doc.GetPage(1).Text);

        var pngDir = IoPath.Combine(dir, "png");
        (code, _, stderr) = WinToolSupport.Cli("--tool", "img-html-to-image", "--in", html, "--out", pngDir);
        Assert.True(code == 0, stderr);
        Assert.True(WebView2Host.LastBlockedCount >= 1, "no request was counted as blocked: " + WebView2Host.LastBlockedCount);
        Assert.True(IoFile.Exists(IoPath.Combine(pngDir, "proof.png")));
    }

    [Fact]
    public void Html_to_png_has_the_asked_width_and_the_block_colour_at_its_position()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "img-html-to-image", "--in", Html(dir), "--out", outDir,
            "--opt", "width=800", "--opt", "full_page=false");
        Assert.True(code == 0, stderr);
        var bytes = IoFile.ReadAllBytes(IoPath.Combine(outDir, "proof.png"));
        Assert.True(WinToolSupport.IsPng(bytes));
        Assert.Equal(800, WinToolSupport.PngSize(bytes).Width);

        using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);
        var inside = img[250, 250];
        Assert.True(inside.R > 240 && inside.G < 15 && inside.B < 15, "pixel at the red block is " + inside);
        var outside = img[600, 500];
        Assert.True(outside.R > 240 && outside.G > 240 && outside.B > 240, "pixel beside the block is " + outside);
    }

    [Fact]
    public void Full_page_png_is_as_tall_as_the_document_and_jpg_is_a_jpeg()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var html = Html(dir, tall: 3000);
        var pngDir = IoPath.Combine(dir, "png");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "img-html-to-image", "--in", html, "--out", pngDir, "--opt", "width=600");
        Assert.True(code == 0, stderr);
        var bytes = IoFile.ReadAllBytes(IoPath.Combine(pngDir, "proof.png"));
        var (w, h) = WinToolSupport.PngSize(bytes);
        Assert.Equal(600, w);
        Assert.InRange(h, 3000, 3100);
        using (var img = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes))
        {
            var bottom = img[50, 2980];
            Assert.True(bottom.B > 240 && bottom.R < 15 && bottom.G < 15, "pixel at the blue block near the bottom is " + bottom);
        }

        var jpgDir = IoPath.Combine(dir, "jpg");
        (code, _, stderr) = WinToolSupport.Cli("--tool", "img-html-to-image", "--in", html, "--out", jpgDir,
            "--opt", "format=jpg", "--opt", "width=600");
        Assert.True(code == 0, stderr);
        Assert.True(WinToolSupport.IsJpeg(IoFile.ReadAllBytes(IoPath.Combine(jpgDir, "proof.jpg"))));
    }

    [Fact]
    public void Bad_input_and_bad_options_fail_naming_the_cause_and_write_nothing()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var html = Html(dir);
        var txt = IoPath.Combine(dir, "notes.txt");
        IoFile.WriteAllText(txt, "hello");

        var outA = IoPath.Combine(dir, "a");
        var r = WinToolSupport.Cli("--tool", "pdf-from-html", "--in", txt, "--out", outA);
        Assert.NotEqual(0, r.Code);
        Assert.False(WinToolSupport.HasFiles(outA));

        var outB = IoPath.Combine(dir, "b");
        r = WinToolSupport.Cli("--tool", "pdf-from-html", "--in", html, "--out", outB, "--opt", "margin=99");
        Assert.NotEqual(0, r.Code);
        Assert.Contains("margin", r.Err);
        Assert.False(WinToolSupport.HasFiles(outB));

        var outC = IoPath.Combine(dir, "c");
        r = WinToolSupport.Cli("--tool", "img-html-to-image", "--in", html, "--out", outC, "--opt", "width=10");
        Assert.NotEqual(0, r.Code);
        Assert.Contains("width", r.Err);
        Assert.False(WinToolSupport.HasFiles(outC));
    }

    [Fact]
    public void Second_run_keeps_the_first_runs_pdf()
    {
        RuntimeIsThere();
        var dir = WinToolSupport.NewDir();
        var html = Html(dir);
        var outDir = IoPath.Combine(dir, "out");
        Assert.Equal(0, WinToolSupport.Cli("--tool", "pdf-from-html", "--in", html, "--out", outDir).Code);
        Assert.Equal(0, WinToolSupport.Cli("--tool", "pdf-from-html", "--in", html, "--out", outDir).Code);
        Assert.Equal(2, IoDirectory.GetFiles(outDir, "*.pdf").Length);
        Assert.Equal(2, IoDirectory.GetFiles(outDir).Length);
    }

    [Fact]
    public async Task A_run_that_never_finishes_times_out_instead_of_hanging()
    {
        RuntimeIsThere();
        var html = Html(WinToolSupport.NewDir());
        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => WebView2Host.RunAsync<int>(html, 400, 300, async _ =>
        {
            await Task.Delay(Timeout.Infinite);
            return 0;
        }, CancellationToken.None, timeoutSeconds: 3));
        Assert.Contains("3 s", ex.Message);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "the timeout took too long");
    }

    [Fact]
    public void Only_file_pages_count_as_local()
    {
        Assert.True(WebView2Host.IsLocal("file:///C:/a/b.html"));
        Assert.True(WebView2Host.IsLocal("data:text/plain;base64,AAAA"));
        Assert.False(WebView2Host.IsLocal("http://example.invalid/x"));
        Assert.False(WebView2Host.IsLocal("https://example.invalid/x"));
        Assert.False(WebView2Host.IsLocal("ws://example.invalid/x"));
        Assert.False(WebView2Host.IsLocal("ftp://example.invalid/x"));
        Assert.False(WebView2Host.IsLocal("//example.invalid/x"));
        Assert.False(WebView2Host.IsLocal("file://attacker/share/x.png"));
    }
}
#endif
