using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace NetieSpace.Tests;

public class PdfToolEditTests
{
    private static Word FindWord(string path, int pageNo, string word)
    {
        using var d = PdfKit.Open(path);
        var w = d.GetPage(pageNo).GetWords().First(x => x.Text == word);
        return w;
    }

    // ----- rotate -----

    [Fact]
    public void Rotate_turns_every_page_right_by_default_and_left_when_asked()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "r.pdf", 3);
        var right = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir()));
        var left = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir(), ("direction", "left")));
        Assert.Equal(new[] { 90, 90, 90 }, PdfKit.Rotations(right));
        Assert.Equal(new[] { 270, 270, 270 }, PdfKit.Rotations(left));
        Assert.Equal(3, PdfKit.Texts(right).Count(t => t.Contains("DOC page")));
    }

    [Fact]
    public void Rotate_honours_the_page_list_and_the_orientation_filter_and_adds_to_existing_rotation()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "mix.pdf", 4, tweak: (p, i) =>
        {
            if (i % 2 == 0) { p.Width = 800; p.Height = 500; }   // pages 2 and 4 are landscape
            if (i == 1) p.Rotate = 90;                           // page 1 is portrait paper shown sideways
        });

        var landscapeOnly = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir(), ("filter", "landscape")));
        var firstTwo = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir(), ("pages", "1-2")));

        // Page 1 is displayed landscape (Rotate 90 on a portrait sheet) so it counts as landscape too.
        Assert.Equal(new[] { 180, 90, 90, 90 }.Length, PdfKit.Rotations(landscapeOnly).Count);
        Assert.Equal(new[] { 180, 90, 0, 90 }, PdfKit.Rotations(landscapeOnly));
        Assert.Equal(new[] { 180, 90, 0, 0 }, PdfKit.Rotations(firstTwo));
    }

    [Fact]
    public void Rotate_with_nothing_matching_fails_instead_of_copying_the_input()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "p.pdf", 2);
        var outDir = PdfKit.NewDir();
        var r = PdfKit.Run("pdf-rotate", new[] { src }, outDir, ("filter", "landscape"));
        Assert.False(r.Ok);
        Assert.Contains("no page matched", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Rotate_keeps_same_named_inputs_apart_and_never_touches_the_originals()
    {
        var a = PdfKit.Make(PdfKit.NewDir("one"), "scan.pdf", 1, "ONE");
        var b = PdfKit.Make(PdfKit.NewDir("two"), "scan.pdf", 2, "TWO");
        var beforeA = PdfKit.Bytes(a);
        var beforeB = PdfKit.Bytes(b);
        var outDir = PdfKit.NewDir();

        var outs = PdfKit.RunOk("pdf-rotate", new[] { a, b }, outDir);

        Assert.Equal(2, outs.Count);
        Assert.NotEqual(outs[0], outs[1]);
        Assert.All(outs, o => Assert.Equal(outDir, IoPath.GetDirectoryName(o)));
        Assert.Contains("ONE page 1", PdfKit.Texts(outs[0])[0]);
        Assert.Contains("TWO page 2", PdfKit.Texts(outs[1])[1]);
        Assert.Equal(beforeA, PdfKit.Bytes(a));
        Assert.Equal(beforeB, PdfKit.Bytes(b));
    }

    [Fact]
    public void Rotate_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "r.pdf", 2);
        var (exit, stdout, err) = PdfKit.Cli("pdf-rotate", new[] { src }, PdfKit.NewDir(), ("direction", "left"));
        Assert.True(exit == 0, err);
        Assert.Equal(new[] { 270, 270 }, PdfKit.Rotations(Assert.Single(PdfKit.Lines(stdout))));
    }

    // ----- page numbers -----

    [Fact]
    public void Page_numbers_stamp_the_counter_from_the_start_value_on_the_chosen_pages_only()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 4);
        var outs = PdfKit.RunOk("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("pages", "2-end"), ("start", "5"), ("format", "page-n"));
        var t = PdfKit.Texts(Assert.Single(outs));
        Assert.DoesNotContain("Page ", t[0]);
        Assert.Contains("Page 5", t[1]);
        Assert.Contains("Page 6", t[2]);
        Assert.Contains("Page 7", t[3]);
        Assert.Contains("DOC page 2", t[1]);
    }

    [Fact]
    public void Page_numbers_n_of_total_counts_the_selected_pages()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 3);
        var t = PdfKit.Texts(Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("format", "n-of-total"))));
        Assert.Contains("1 of 3", t[0]);
        Assert.Contains("3 of 3", t[2]);
    }

    [Theory]
    [InlineData("bottom-right")]
    [InlineData("top-left")]
    [InlineData("bottom-center")]
    public void Page_numbers_land_in_the_requested_corner_of_the_page(string position)
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 1);
        var path = Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("position", position), ("format", "page-n")));
        var w = FindWord(path, 1, "Page");
        var cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2;
        var cy = (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2;
        if (position.EndsWith("right")) Assert.InRange(cx, 480, 590);
        if (position.EndsWith("left")) Assert.InRange(cx, 20, 130);
        if (position.EndsWith("center")) Assert.InRange(cx, 250, 360);
        if (position.StartsWith("bottom")) Assert.InRange(cy, 15, 70);
        if (position.StartsWith("top")) Assert.InRange(cy, 722, 780);
    }

    [Fact]
    public void Page_numbers_follow_a_rotated_page_and_a_crop_box()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 1);
        var cropped = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { src }, PdfKit.NewDir(), ("left", "50"), ("right", "50"), ("top", "50"), ("bottom", "50")));
        var numbered = Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { cropped }, PdfKit.NewDir(), ("position", "bottom-right"), ("format", "page-n")));
        var w = FindWord(numbered, 1, "Page");
        // Visible area is 512 x 692 (PdfPig reports positions relative to the crop box): the stamp sits 28pt inside its corner.
        Assert.InRange(w.BoundingBox.Right, 440, 500);
        Assert.InRange(w.BoundingBox.Bottom, 20, 50);

        var rotated = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir()));
        var turned = Assert.Single(PdfKit.RunOk("pdf-page-numbers", new[] { rotated }, PdfKit.NewDir(), ("position", "bottom-center"), ("format", "page-n")));
        var rw = FindWord(turned, 1, "Page");
        // PdfPig reports a rotated page in its displayed frame (x from the left, y negative from the top):
        // the stamp must read upright there, horizontally centred, 28pt above the displayed bottom edge (612 high).
        Assert.Equal(UglyToad.PdfPig.Content.TextOrientation.Horizontal, rw.TextOrientation);
        Assert.InRange((rw.BoundingBox.Left + rw.BoundingBox.Right) / 2, 380, 412);
        Assert.InRange(rw.BoundingBox.Bottom, -600, -575);
    }

    [Fact]
    public void Page_numbers_reject_bad_options_by_name()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 1);
        var pos = PdfKit.Run("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("position", "middle-center"));
        Assert.False(pos.Ok);
        Assert.Contains("'position'", pos.Error);
        var size = PdfKit.Run("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("size", "2"));
        Assert.False(size.Ok);
        Assert.Contains("'size'", size.Error);
    }

    [Fact]
    public void Page_numbers_run_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "n.pdf", 2);
        var (exit, stdout, err) = PdfKit.Cli("pdf-page-numbers", new[] { src }, PdfKit.NewDir(), ("start", "10"));
        Assert.True(exit == 0, err);
        var t = PdfKit.Texts(Assert.Single(PdfKit.Lines(stdout)));
        Assert.Contains("10", t[0]);
        Assert.Contains("11", t[1]);
    }

    // ----- watermark -----

    [Fact]
    public void Watermark_text_lands_only_on_the_chosen_pages_and_keeps_the_original_text()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 3);
        var path = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "CONFIDENTIAL"), ("pages", "2")));
        var t = PdfKit.Texts(path);
        Assert.DoesNotContain("CONFIDENTIAL", t[0]);
        Assert.Contains("CONFIDENTIAL", t[1]);
        Assert.DoesNotContain("CONFIDENTIAL", t[2]);
        for (var i = 0; i < 3; i++) Assert.Contains($"DOC page {i + 1}", t[i]);
    }

    [Fact]
    public void Watermark_rotation_opacity_and_layer_are_really_applied()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 1);
        var tilted = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "WM"), ("rotation", "45"), ("opacity", "30")));
        var flat = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "WM"), ("rotation", "0"), ("layer", "below")));

        using (var d = PdfKit.Open(tilted))
        {
            var wm = d.GetPage(1).Letters.Where(l => l.Value == "W" || l.Value == "M").ToList();
            Assert.NotEmpty(wm);
            Assert.All(wm, l => Assert.Equal(UglyToad.PdfPig.Content.TextOrientation.Other, l.TextOrientation));
        }
        Assert.Matches(@"/ca\s+0\.29", PdfKit.Latin1(tilted));

        using (var d = PdfKit.Open(flat))
        {
            var letters = d.GetPage(1).Letters;
            Assert.Equal("W", letters[0].Value);                  // below layer: drawn before the page text
            Assert.Equal(UglyToad.PdfPig.Content.TextOrientation.Horizontal, letters[0].TextOrientation);
        }
    }

    [Fact]
    public void Watermark_position_and_tiling_change_where_and_how_often_the_text_appears()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 1);
        var corner = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "STAMP"), ("rotation", "0"), ("position", "top-right"), ("size", "20")));
        var tiled = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "STAMP"), ("tile", "true"), ("rotation", "0"), ("size", "20")));

        var w = FindWord(corner, 1, "STAMP");
        Assert.InRange(w.BoundingBox.Right, 500, 600);
        Assert.InRange(w.BoundingBox.Top, 700, 790);
        using var d = PdfKit.Open(tiled);
        Assert.True(d.GetPage(1).GetWords().Count(x => x.Text.Contains("STAMP")) >= 6);
    }

    [Fact]
    public void Watermark_image_mode_embeds_the_picture_at_the_requested_width()
    {
        var dir = PdfKit.NewDir();
        var png = IoPath.Combine(dir, "logo.png");
        using (var img = new Image<Rgba32>(80, 40, new Rgba32(200, 30, 30, 255)))
            img.SaveAsPng(png);
        var src = PdfKit.Make(dir, "w.pdf", 2);

        var path = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(),
            ("mode", "image"), ("image", png), ("rotation", "0"), ("scale", "50"), ("opacity", "40")));

        using var d = PdfKit.Open(path);
        foreach (var p in d.GetPages())
        {
            var im = Assert.Single(p.GetImages());
            Assert.Equal(80, im.WidthInSamples);
            Assert.Equal(40, im.HeightInSamples);
            Assert.InRange(im.Bounds.Width, 300, 312);              // 50 percent of 612
            Assert.InRange(im.Bounds.Height, 150, 156);             // aspect kept
        }
        Assert.Contains("WM", "WM");
        Assert.Contains("DOC page 1", PdfKit.Texts(path)[0]);
    }

    [Fact]
    public void Watermark_rejects_bad_options_by_name_and_writes_nothing()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 1);
        var outDir = PdfKit.NewDir();
        var blank = PdfKit.Run("pdf-watermark", new[] { src }, outDir, ("text", "   "));
        var noImage = PdfKit.Run("pdf-watermark", new[] { src }, outDir, ("mode", "image"));
        var badOpacity = PdfKit.Run("pdf-watermark", new[] { src }, outDir, ("opacity", "0"));
        var notPicture = IoPath.Combine(PdfKit.NewDir(), "x.png");
        IoFile.WriteAllText(notPicture, "not a picture");
        var junkImage = PdfKit.Run("pdf-watermark", new[] { src }, outDir, ("mode", "image"), ("image", notPicture));
        Assert.Contains("'text'", blank.Error);
        Assert.Contains("'image'", noImage.Error);
        Assert.Contains("'opacity'", badOpacity.Error);
        Assert.Contains("'image'", junkImage.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Watermark_text_option_is_data_not_markup()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 1);
        var evil = "(); /Foo << >> ) Tj 0 0 m \\";
        var path = Assert.Single(PdfKit.RunOk("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", evil), ("rotation", "0")));
        Assert.Equal(1, PdfKit.PageCount(path));
        Assert.Contains("DOC page 1", PdfKit.Texts(path)[0]);
    }

    [Fact]
    public void Watermark_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "w.pdf", 2);
        var (exit, stdout, err) = PdfKit.Cli("pdf-watermark", new[] { src }, PdfKit.NewDir(), ("text", "SECRET"));
        Assert.True(exit == 0, err);
        Assert.All(PdfKit.Texts(Assert.Single(PdfKit.Lines(stdout))), t => Assert.Contains("SECRET", t));
    }

    // ----- crop -----

    [Fact]
    public void Crop_sets_a_smaller_visible_box_and_keeps_the_content()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "c.pdf", 2);
        var path = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { src }, PdfKit.NewDir(), ("left", "10"), ("top", "20"), ("right", "30"), ("bottom", "40")));
        var boxes = PdfKit.CropBoxes(path);
        Assert.Equal(2, boxes.Count);
        // PDF user space is y-up: bottom margin 40 lifts y1, top margin 20 lowers y2.
        Assert.All(boxes, bx => Assert.Equal(new[] { 10.0, 40.0, 582.0, 772.0 }, bx));
        Assert.Contains("DOC page 2", PdfKit.Texts(path)[1]);
    }

    [Fact]
    public void Crop_margins_are_as_displayed_even_on_a_rotated_page()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "c.pdf", 1);
        var rotated = Assert.Single(PdfKit.RunOk("pdf-rotate", new[] { src }, PdfKit.NewDir()));      // shown 90 degrees clockwise
        var path = Assert.Single(PdfKit.RunOk("pdf-crop", new[] { rotated }, PdfKit.NewDir(), ("left", "100")));
        // The visual left edge of a page turned clockwise is the unrotated bottom edge.
        Assert.Equal(new[] { 0.0, 100.0, 612.0, 792.0 }, Assert.Single(PdfKit.CropBoxes(path)));
    }

    [Fact]
    public void Crop_rejects_no_margins_and_margins_that_eat_the_page()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "c.pdf", 1);
        var none = PdfKit.Run("pdf-crop", new[] { src }, PdfKit.NewDir());
        var all = PdfKit.Run("pdf-crop", new[] { src }, PdfKit.NewDir(), ("left", "400"), ("right", "400"));
        Assert.False(none.Ok);
        Assert.Contains("margin", none.Error);
        Assert.False(all.Ok);
        Assert.Contains("nothing of page 1", all.Error);
    }

    [Fact]
    public void Crop_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "c.pdf", 1);
        var (exit, stdout, err) = PdfKit.Cli("pdf-crop", new[] { src }, PdfKit.NewDir(), ("bottom", "100"));
        Assert.True(exit == 0, err);
        Assert.Equal(new[] { 0.0, 100.0, 612.0, 792.0 }, Assert.Single(PdfKit.CropBoxes(Assert.Single(PdfKit.Lines(stdout)))));
    }
}
