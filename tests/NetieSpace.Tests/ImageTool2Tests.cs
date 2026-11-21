using NetieSpace.Apps.Image;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Text watermark and meme: asserted by pixels in the file a person receives.</summary>
public class ImageTool2TextTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();
    private static readonly Rgba32 White = new(255, 255, 255, 255);
    private static readonly Rgba32 Grey = new(128, 128, 128, 255);

    private static int CountDiffering(Image<Rgba32> img, Rectangle area, Rgba32 baseline)
    {
        var n = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                if (!img[x, y].Equals(baseline)) n++;
        return n;
    }

    private static Rectangle InkBounds(Image<Rgba32> img, Rgba32 baseline)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (!img[x, y].Equals(baseline))
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ---- watermark text --------------------------------------------------------------------------

    [Fact]
    public async Task Text_watermark_changes_the_centre_and_leaves_far_regions_alone()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 400, 200, White);
        var r = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"),
            ("text", "DRAFT"), ("color", "#000000"), ("opacity", "100"), ("scale", "50"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.Equal((400, 200), (o.Width, o.Height));
        Assert.True(CountDiffering(o, new Rectangle(100, 50, 200, 100), White) > 200, "centre must carry ink");
        Assert.Equal(0, CountDiffering(o, new Rectangle(0, 0, 60, 40), White));
        Assert.Equal(0, CountDiffering(o, new Rectangle(340, 160, 60, 40), White));
        // Solid glyph interior reaches the full colour, so it is the text colour and not a smear.
        var ink = InkBounds(o, White);
        Assert.True(ink.Width > 120 && ink.Width < 260, "ink width follows the mark size: " + ink);
        var dark = 0;
        for (var y = ink.Top; y < ink.Bottom; y++)
            for (var x = ink.Left; x < ink.Right; x++)
                if (o[x, y].R < 40) dark++;
        Assert.True(dark > 100, "fully covered pixels carry the exact colour");
    }

    [Fact]
    public async Task Text_watermark_uses_the_requested_colour_and_opacity()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 300, 150, White);
        var red = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "r"),
            ("text", "MMMM"), ("color", "#FF0000"), ("opacity", "100"), ("scale", "60"));
        Assert.True(red.Ok, red.Error);
        using (var o = Open(red.Outputs[0]))
        {
            var found = false;
            for (var y = 0; y < o.Height && !found; y++)
                for (var x = 0; x < o.Width; x++)
                    if (o[x, y].Equals(Red)) { found = true; break; }
            Assert.True(found, "some pixels are pure red");
        }
        var half = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "h"),
            ("text", "MMMM"), ("color", "#000000"), ("opacity", "50"), ("scale", "60"));
        Assert.True(half.Ok, half.Error);
        using var h = Open(half.Outputs[0]);
        byte darkest = 255;
        for (var y = 0; y < h.Height; y++)
            for (var x = 0; x < h.Width; x++) darkest = Math.Min(darkest, h[x, y].R);
        Assert.InRange((int)darkest, 120, 136);
    }

    [Fact]
    public async Task Text_watermark_position_moves_the_ink()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 400, 300, White);
        var nw = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "a"),
            ("text", "HI"), ("color", "#000000"), ("opacity", "100"), ("scale", "20"), ("position", "top-left"));
        var se = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "b"),
            ("text", "HI"), ("color", "#000000"), ("opacity", "100"), ("scale", "20"), ("position", "bottom-right"));
        Assert.True(nw.Ok, nw.Error); Assert.True(se.Ok, se.Error);
        using var a = Open(nw.Outputs[0]); using var b = Open(se.Outputs[0]);
        var ia = InkBounds(a, White); var ib = InkBounds(b, White);
        Assert.True(ia.Right < 150 && ia.Bottom < 100, "top-left ink: " + ia);
        Assert.True(ib.Left > 250 && ib.Top > 200, "bottom-right ink: " + ib);
    }

    [Fact]
    public async Task Text_watermark_with_odd_characters_just_draws_them()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 300, 120, White);
        var r = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"),
            ("text", "a'b:c%d\\e/f,\"g;[x]"), ("color", "#000000"), ("opacity", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.True(CountDiffering(o, new Rectangle(0, 0, 300, 120), White) > 100);
    }

    [Fact]
    public async Task Text_watermark_with_a_newline_makes_a_taller_block()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 300, 300, White);
        var one = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "a"),
            ("text", "AB"), ("color", "#000000"), ("opacity", "100"), ("scale", "30"));
        var two = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "b"),
            ("text", "AB\nAB"), ("color", "#000000"), ("opacity", "100"), ("scale", "30"));
        Assert.True(one.Ok, one.Error); Assert.True(two.Ok, two.Error);
        using var a = Open(one.Outputs[0]); using var b = Open(two.Outputs[0]);
        Assert.True(InkBounds(b, White).Height > InkBounds(a, White).Height * 1.6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public async Task Text_watermark_with_blank_text_is_refused_and_writes_nothing(string text)
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 100, 50, White);
        var outDir = IoPath.Combine(d, "o");
        var r = await Run("img-watermark", new[] { src }, outDir, ("text", text));
        Assert.False(r.Ok);
        Assert.Contains("'text'", r.Error);
        Assert.False(IoDirectory.Exists(outDir) && IoDirectory.GetFiles(outDir).Length > 0);
    }

    [Fact]
    public async Task Text_watermark_rejects_bad_options_naming_them()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 100, 50, White);
        var mark = SolidImage(d, "m.png", 10, 10, Rgba32.ParseHex("000000FF"));
        var both = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", "x"), ("image", mark));
        Assert.False(both.Ok);
        Assert.Contains("not both", both.Error);
        var color = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", "x"), ("color", "red"));
        Assert.False(color.Ok);
        Assert.Contains("'color'", color.Error);
        var ctl = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", "x\u0001y"));
        Assert.False(ctl.Ok);
        Assert.Contains("control character", ctl.Error);
        var longText = await Run("img-watermark", new[] { src }, IoPath.Combine(d, "o"), ("text", new string('x', 501)));
        Assert.False(longText.Ok);
        Assert.Contains("'text'", longText.Error);
    }

    [Fact]
    public async Task Text_watermark_keeps_the_input_and_names_distinct_outputs()
    {
        var d = Dir();
        var a = SolidImage(IoPath.Combine(d, "one"), "same.png", 100, 50, White);
        var b = SolidImage(IoPath.Combine(d, "two"), "same.png", 100, 50, White);
        var before = Sha(a);
        var outDir = IoPath.Combine(d, "o");
        var r = await Run("img-watermark", new[] { a, b }, outDir, ("text", "X"), ("color", "#000000"), ("opacity", "100"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Outputs.Distinct().Count());
        Assert.All(r.Outputs, p => Assert.Equal(outDir, IoPath.GetDirectoryName(p)));
        Assert.Equal(before, Sha(a));
    }

    // ---- meme ------------------------------------------------------------------------------------

    [Fact]
    public async Task Meme_draws_white_outlined_captions_top_and_bottom_and_nothing_in_the_middle()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 400, 300, Grey);
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "when it works"), ("bottom", "on friday"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.Equal((400, 300), (o.Width, o.Height));
        int whites = 0, blacks = 0;
        for (var y = 0; y < 90; y++)
            for (var x = 0; x < 400; x++)
            {
                if (o[x, y].Equals(White)) whites++;
                if (o[x, y].R < 8 && o[x, y].G < 8) blacks++;
            }
        Assert.True(whites > 150, "white fill in the top band: " + whites);
        Assert.True(blacks > 150, "black outline in the top band: " + blacks);
        Assert.True(CountDiffering(o, new Rectangle(0, 210, 400, 90), Grey) > 300, "bottom caption present");
        Assert.Equal(0, CountDiffering(o, new Rectangle(0, 100, 400, 100), Grey));
        Assert.Equal(0, CountDiffering(o, new Rectangle(0, 90, 4, 120), Grey));
    }

    [Fact]
    public async Task Meme_with_only_a_top_caption_leaves_the_bottom_alone()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 400, 300, Grey);
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"), ("top", "just top"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.True(CountDiffering(o, new Rectangle(0, 0, 400, 100), Grey) > 300);
        Assert.Equal(0, CountDiffering(o, new Rectangle(0, 100, 400, 200), Grey));
    }

    [Fact]
    public async Task Meme_long_caption_wraps_and_stays_inside_the_image()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 300, 300, Grey);
        var r = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"),
            ("top", "this is a rather long caption that cannot fit on one line"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        var ink = InkBounds(o, Grey);
        Assert.True(ink.Left >= 0 && ink.Right <= 300 && ink.Width > 100, "ink: " + ink);
        Assert.True(ink.Bottom < 110, "caption stays in the top third: " + ink);
        Assert.True(ink.Height > 30, "it wrapped onto more than one line: " + ink);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "ok")]
    [InlineData("ok", "  \t")]
    public async Task Meme_refuses_an_empty_or_blank_caption(string top, string bottom)
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 200, 100, Grey);
        var outDir = IoPath.Combine(d, "o");
        var r = await Run("img-meme", new[] { src }, outDir, ("top", top), ("bottom", bottom));
        Assert.False(r.Ok);
        Assert.Matches("'(top|bottom)'", r.Error);
        Assert.False(IoDirectory.Exists(outDir) && IoDirectory.GetFiles(outDir).Length > 0);
    }

    [Fact]
    public async Task Meme_needs_at_least_one_caption_and_refuses_text_that_cannot_fit()
    {
        var d = Dir();
        var src = SolidImage(d, "b.png", 200, 100, Grey);
        var none = await Run("img-meme", new[] { src }, IoPath.Combine(d, "o"));
        Assert.False(none.Ok);
        Assert.Contains("'top'", none.Error);
        var tiny = SolidImage(d, "t.png", 60, 40, Grey);
        var huge = await Run("img-meme", new[] { tiny }, IoPath.Combine(d, "o"), ("top", new string('W', 400)));
        Assert.False(huge.Ok);
        Assert.Contains("too long", huge.Error);
    }

    [Fact]
    public async Task Meme_keeps_the_format_and_flattens_gif_to_one_captioned_frame()
    {
        var d = Dir();
        var jpg = NoisyImage(d, "n.jpg", "jpg", 200, 120);
        var rj = await Run("img-meme", new[] { jpg }, IoPath.Combine(d, "o"), ("bottom", "jpeg"));
        Assert.True(rj.Ok, rj.Error);
        Assert.True(IsJpg(rj.Outputs[0]));

        var gif = IoPath.Combine(d, "anim.gif");
        using (var anim = new Image<Rgba32>(120, 80, Grey))
        {
            using var f2 = new Image<Rgba32>(120, 80, Blue);
            anim.Frames.AddFrame(f2.Frames.RootFrame);
            anim.Save(gif, new SixLabors.ImageSharp.Formats.Gif.GifEncoder());
        }
        var rg = await Run("img-meme", new[] { gif }, IoPath.Combine(d, "g"), ("top", "frames"));
        Assert.True(rg.Ok, rg.Error);
        using var og = SixLabors.ImageSharp.Image.Load(rg.Outputs[0]);
        Assert.Equal(1, og.Frames.Count);
    }

    // ---- the --tool path --------------------------------------------------------------------------

    private static string OneOutput((int Exit, string Out, string Err) r)
    {
        Assert.True(r.Exit == 0, r.Err);
        var path = Assert.Single(r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')));
        Assert.True(IoFile.Exists(path));
        return path;
    }

    [Fact]
    public void Cli_watermark_text()
    {
        var src = SolidImage(Dir(), "a.png", 200, 100, White);
        var o = OneOutput(Cli("--tool", "img-watermark", "--in", src, "--out", Dir(),
            "--opt", "text=SAMPLE", "--opt", "color=#000000", "--opt", "opacity=100"));
        using var img = Open(o);
        Assert.True(CountDiffering(img, new Rectangle(0, 0, 200, 100), White) > 100);
        Assert.Equal(0, CountDiffering(img, new Rectangle(0, 0, 30, 20), White));
    }

    [Fact]
    public void Cli_meme()
    {
        var src = SolidImage(Dir(), "a.png", 300, 200, Grey);
        var o = OneOutput(Cli("--tool", "img-meme", "--in", src, "--out", Dir(), "--opt", "top=hello", "--opt", "bottom=world"));
        using var img = Open(o);
        Assert.True(CountDiffering(img, new Rectangle(0, 0, 300, 70), Grey) > 100);
        Assert.True(CountDiffering(img, new Rectangle(0, 130, 300, 70), Grey) > 100);
        Assert.Equal(0, CountDiffering(img, new Rectangle(0, 70, 300, 60), Grey));
    }

    [Fact]
    public void Cli_meme_with_blank_caption_fails_with_a_message()
    {
        var src = SolidImage(Dir(), "a.png", 300, 200, Grey);
        var r = Cli("--tool", "img-meme", "--in", src, "--out", Dir(), "--opt", "top= ");
        Assert.NotEqual(0, r.Exit);
        Assert.Contains("'top'", r.Err);
    }

    [Fact]
    public void A_system_font_is_resolved_on_this_machine()
    {
        // Fails loudly (does not skip) when the machine has no usable font: text tools would be useless.
        var fam = ImageText.ResolveFamily();
        Assert.False(string.IsNullOrEmpty(fam.Name));
    }
}
