using NetieSpace.Apps.Image;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Adversarial checks of the text watermark and meme runners, by the files a person receives.</summary>
public class ImageTool2VerifyTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();
    private static readonly Rgba32 White = new(255, 255, 255, 255);

    private static int Ink(Image<Rgba32> img, Rgba32 baseline)
    {
        var n = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (!img[x, y].Equals(baseline)) n++;
        return n;
    }

    [Theory]
    [InlineData("a\"b:c%d'e/f\\g;h,[i]=j")]
    [InlineData("$(rm -rf /) `x` & | > < ; \" ' %USERPROFILE% ..\\..\\x")]
    [InlineData("\u00DCn\u00EFc\u00F6d\u00E9 \u03A9mega")]
    [InlineData("line one\nline two")]
    public async Task Hostile_text_watermark_renders_ink_and_nothing_else(string text)
    {
        var d = Dir();
        var src = SolidImage(d, "it's a \u00FCn\u00EF file.png", 301, 177, White);
        var r = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "new", "deeper"),
            ("text", text), ("color", "#000000"), ("opacity", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.Equal((301, 177), (o.Width, o.Height));
        Assert.True(Ink(o, White) > 50);
        Assert.Equal(IoPath.Combine(d, "new", "deeper"), IoPath.GetDirectoryName(r.Outputs[0]));
    }

    [Fact]
    public async Task Text_watermark_scale_and_opacity_beyond_limits_are_refused()
    {
        var d = Dir();
        var src = SolidImage(d, "a.png", 100, 100, White);
        foreach (var (k, v) in new[] { ("opacity", "500"), ("opacity", "0"), ("scale", "0"), ("scale", "1000"), ("opacity", "abc") })
        {
            var r = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", "hi"), (k, v));
            Assert.False(r.Ok, k + "=" + v + " should be refused");
            Assert.Contains(k, r.Error ?? "");
        }
    }

    [Fact]
    public async Task Text_watermark_on_a_tiny_image_does_not_crash()
    {
        var d = Dir();
        var src = SolidImage(d, "t.png", 3, 2, White);
        var r = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", "WWWWWWWW"), ("scale", "100"), ("color", "#000000"), ("opacity", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.Equal((3, 2), (o.Width, o.Height));
    }

    [Fact]
    public async Task Same_named_inputs_from_two_folders_do_not_overwrite_for_text_watermark_and_meme()
    {
        var d = Dir();
        var a = SolidImage(IoPath.Combine(d, "a"), "same.png", 120, 80, Red);
        var b = SolidImage(IoPath.Combine(d, "b"), "same.png", 90, 60, Blue);
        var r = await Run("img-watermark", new[] { a, b }, IoPath.Combine(d, "o"), ("text", "X"), ("opacity", "100"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Outputs.Distinct().Count());
        using (var o = Open(r.Outputs[0])) Assert.Equal((120, 80), (o.Width, o.Height));
        using (var o = Open(r.Outputs[1])) Assert.Equal((90, 60), (o.Width, o.Height));
        var m = await Run("img-meme", new[] { a }, IoPath.Combine(d, "o"), ("top", "x"));
        Assert.True(m.Ok, m.Error);
        Assert.DoesNotContain(m.Outputs[0], r.Outputs);
        Assert.NotEqual(a, m.Outputs[0]);
    }

    [Theory]
    [InlineData("img-watermark")]
    [InlineData("img-meme")]
    public async Task Zero_byte_and_corrupt_and_truncated_inputs_fail_clearly_with_no_output(string tool)
    {
        var d = Dir();
        var zero = IoPath.Combine(d, "zero.png"); IoFile.WriteAllBytes(zero, Array.Empty<byte>());
        var junk = IoPath.Combine(d, "junk.png"); IoFile.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var good = SolidImage(d, "good.png", 200, 100, White);
        var bytes = IoFile.ReadAllBytes(good);
        var trunc = IoPath.Combine(d, "trunc.png"); IoFile.WriteAllBytes(trunc, bytes.Take(bytes.Length / 2).ToArray());
        var hdr = IoPath.Combine(d, "hdr.png"); IoFile.WriteAllBytes(hdr, bytes.Take(20).ToArray());
        foreach (var f in new[] { zero, junk, trunc, hdr })
        {
            var o = IoPath.Combine(d, "out-" + IoPath.GetFileNameWithoutExtension(f));
            var r = await Run(tool, new[] { f }, o, tool == "img-meme" ? ("top", "hi") : ("text", "hi"));
            Assert.False(r.Ok, f);
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            Assert.True(!IoDirectory.Exists(o) || IoDirectory.GetFiles(o).Length == 0, "no output for " + f);
        }
    }

    [Fact]
    public async Task Meme_on_odd_portrait_and_jpeg_and_gif_inputs()
    {
        var d = Dir();
        foreach (var (name, w, h, fmt) in new[] { ("p.jpg", 333, 777, "jpg"), ("q.png", 77, 131, "png"), ("g.gif", 150, 100, "gif"), ("b.bmp", 201, 99, "bmp") })
        {
            var src = SolidImage(d, name, w, h, new Rgba32(30, 120, 200, 255), fmt);
            var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "Top text here"), ("bottom", "bottom\nline two"));
            Assert.True(r.Ok, name + " " + r.Error);
            using var o = Open(r.Outputs[0]);
            Assert.Equal((w, h), (o.Width, o.Height));
            Assert.True(Math.Abs(o[w / 2, h / 2].B - 200) < 20, name + " centre untouched");
            var white = 0;
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) if (o[x, y].R > 240 && o[x, y].G > 240) white++;
            Assert.True(white > 20, name + " has white glyph pixels");
        }
    }

    [Fact]
    public async Task Meme_hostile_text_and_one_pixel_image()
    {
        var d = Dir();
        var src = SolidImage(d, "m.png", 400, 300, White);
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "\"; rm -rf % : ' \\ /"), ("bottom", "\u00FCn\u00EF \u00DF"));
        Assert.True(r.Ok, r.Error);
        var tiny = SolidImage(d, "one.png", 1, 1, White);
        var t = await Run("img-meme", new[] { tiny }, IoPath.Combine(d, "o2"), ("top", "hello"));
        if (t.Ok) { using var o = Open(t.Outputs[0]); Assert.Equal((1, 1), (o.Width, o.Height)); }
        else Assert.False(string.IsNullOrWhiteSpace(t.Error));
    }

    [Fact]
    public async Task Meme_long_caption_either_fits_inside_its_band_or_is_refused()
    {
        var d = Dir();
        var src = SolidImage(d, "m.png", 600, 400, White);
        var text = string.Join(" ", Enumerable.Repeat("WORD", 90));
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("bottom", text));
        if (r.Ok)
        {
            using var o = Open(r.Outputs[0]);
            for (var y = 0; y < 400 * 0.65; y++)
                for (var x = 0; x < 600; x++)
                    Assert.True(o[x, y].Equals(White), "ink leaked above the bottom band at " + x + "," + y);
        }
        else Assert.Contains("too long", r.Error);
    }

    [Fact]
    public async Task Meme_on_a_large_image_finishes_in_reasonable_time()
    {
        var d = Dir();
        var src = SolidImage(d, "big.png", 6000, 4000, White);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "HI"), ("bottom", "THERE"));
        Assert.True(r.Ok, r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "took " + sw.Elapsed);
    }

    [Theory]
    [InlineData("img-watermark")]
    [InlineData("img-meme")]
    public async Task Pre_cancelled_run_is_cancelled_and_writes_nothing(string tool)
    {
        var d = Dir();
        var src = SolidImage(d, "a.png", 100, 100, White);
        var t = Registry().Find(tool)!;
        var cts = new CancellationTokenSource(); cts.Cancel();
        var opts = new Dictionary<string, string> { [tool == "img-meme" ? "top" : "text"] = "hi" };
        var o = IoPath.Combine(d, "o");
        var r = await ToolRunner.RunAsync(t, new ToolRequest(new[] { src }, o, opts), null, cts.Token);
        Assert.False(r.Ok);
        Assert.True(!IoDirectory.Exists(o) || IoDirectory.GetFiles(o).Length == 0);
    }

    [Fact]
    public void Cli_hostile_text_goes_through_unchanged_and_renders()
    {
        var d = Dir();
        var src = SolidImage(d, "c.png", 200, 100, White);
        var r = Cli("--tool", "img-watermark", "--in", src, "--out", IoPath.Combine(d, "o"),
            "--opt", "text=a=b;c:d\"e%f", "--opt", "color=#000000", "--opt", "opacity=100");
        Assert.Equal(0, r.Exit);
        var f = IoDirectory.GetFiles(IoPath.Combine(d, "o")).Single();
        using var o = Open(f);
        Assert.True(Ink(o, White) > 50);
    }

    [Fact]
    public async Task Meme_on_an_eight_thousand_square_image_finishes()
    {
        var d = Dir();
        var src = SolidImage(d, "big.png", 8000, 8000, White);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "HI"));
        Assert.True(r.Ok, r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), "took " + sw.Elapsed);
    }

    [Theory]
    [InlineData("img-watermark", "text")]
    [InlineData("img-meme", "top")]
    public async Task Text_the_font_cannot_draw_is_refused_not_drawn_as_boxes(string tool, string key)
    {
        var d = Dir();
        var src = SolidImage(d, "a.png", 300, 200, White);
        // Private-use code points have no glyph in any installed font.
        var r = await Run(tool, new[] { src }, IoPath.Combine(d, "o"), (key, "\uE000\uE001\uE002"));
        Assert.False(r.Ok, "tofu must not be delivered as a result");
        Assert.Contains("glyph", r.Error ?? "");
    }
}
