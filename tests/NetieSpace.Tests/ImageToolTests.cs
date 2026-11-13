using NetieSpace.Apps.Image;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

public class ImageToolTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();

    // ---- registration and availability ----------------------------------------------------------

    private static readonly string[] Built =
    {
        "img-resize", "img-crop", "img-rotate", "img-compress", "img-to-jpg",
        "img-from-jpg", "img-convert-any", "img-watermark", "img-dpi",
    };

    [Fact]
    public void Built_image_tools_are_available_on_a_cleared_in_process_engine()
    {
        var reg = Registry();
        foreach (var id in Built)
        {
            var t = reg.Find(id)!;
            Assert.True(t.Availability.Available, id + ": " + t.Availability.Text);
            Assert.NotNull(t.Runner);
            Assert.Equal(EngineLicense.Apache2, t.Entry.Engine.License);
            Assert.Contains("ImageSharp", t.Entry.Engine.Name);
        }
    }

    [Theory]
    [InlineData("img-upscale")]
    [InlineData("img-remove-bg")]
    public void Model_tools_stay_unbuilt_and_never_claim_to_work(string id)
    {
        var t = Registry().Find(id)!;
        Assert.False(t.Availability.Available);
        Assert.Null(t.Runner);
    }

    [Fact]
    public void Catalog_still_has_16_image_entries_and_no_agpl_engine()
    {
        Assert.Equal(16, ImageCatalog.Entries.Count);
        Assert.DoesNotContain(ImageCatalog.Entries, e => e.Engine.License == EngineLicense.AGPL);
    }

    // ---- resize ---------------------------------------------------------------------------------

    [Fact]
    public async Task Resize_by_width_keeps_ratio_and_writes_a_png_of_the_new_size()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-resize", new[] { src }, Dir(), ("width", "20"));
        Assert.True(r.Ok, r.Error);
        var o = Assert.Single(r.Outputs);
        Assert.True(IsPng(o));
        var info = SImage.Identify(o);
        Assert.Equal((20, 10), (info.Width, info.Height));
    }

    [Fact]
    public async Task Resize_by_percentage_scales_both_sides()
    {
        var src = CornerImage(Dir(), "pic.png");
        var up = await Run("img-resize", new[] { src }, Dir(), ("mode", "percentage"), ("percent", "200"));
        Assert.True(up.Ok, up.Error);
        Assert.Equal((80, 40), (SImage.Identify(up.Outputs[0]).Width, SImage.Identify(up.Outputs[0]).Height));
        var down = await Run("img-resize", new[] { src }, Dir(), ("mode", "percentage"), ("percent", "25"));
        Assert.Equal((10, 5), (SImage.Identify(down.Outputs[0]).Width, SImage.Identify(down.Outputs[0]).Height));
    }

    [Fact]
    public async Task Resize_without_keep_ratio_forces_the_exact_box_and_keeps_the_halves_apart()
    {
        var dir = Dir();
        var src = IoPath.Combine(dir, "halves.png");
        using (var halves = new Image<Rgba32>(40, 20))
        {
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 40; x++) halves[x, y] = x < 20 ? Red : Blue;
            halves.SaveAsPng(src);
        }
        var r = await Run("img-resize", new[] { src }, Dir(), ("width", "10"), ("height", "30"), ("keep_ratio", "false"));
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((10, 30), (img.Width, img.Height));
        AssertClose(Red, img[1, 15], 40, "left half");
        AssertClose(Blue, img[8, 15], 40, "right half");
    }

    [Fact]
    public async Task Resize_keeps_the_jpg_format_of_a_jpg_input()
    {
        var src = NoisyImage(Dir(), "photo.jpg", "jpg");
        var r = await Run("img-resize", new[] { src }, Dir(), ("width", "50"));
        Assert.True(r.Ok, r.Error);
        Assert.True(IsJpg(r.Outputs[0]));
        Assert.Equal((50, 50), (SImage.Identify(r.Outputs[0]).Width, SImage.Identify(r.Outputs[0]).Height));
    }

    [Fact]
    public async Task Resize_with_neither_width_nor_height_names_the_option()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-resize", new[] { src }, Dir());
        Assert.False(r.Ok);
        Assert.Contains("width", r.Error);
    }

    [Fact]
    public async Task Resize_rejects_an_absurd_output_size()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-resize", new[] { src }, Dir(), ("width", "30000"), ("height", "30000"), ("keep_ratio", "false"));
        Assert.False(r.Ok);
        Assert.Contains("larger than the limit", r.Error);
    }

    [Fact]
    public async Task Two_inputs_with_the_same_file_name_get_two_distinct_outputs()
    {
        var a = CornerImage(Dir(), "same.png", 40, 20);
        var b = CornerImage(Dir(), "same.png", 60, 30);
        var r = await Run("img-resize", new[] { a, b }, Dir(), ("mode", "percentage"), ("percent", "50"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Outputs.Count);
        Assert.Equal(2, r.Outputs.Select(p => p.ToLowerInvariant()).Distinct().Count());
        var sizes = r.Outputs.Select(p => (SImage.Identify(p).Width, SImage.Identify(p).Height)).OrderBy(s => s.Width).ToArray();
        Assert.Equal(new[] { (20, 10), (30, 15) }, sizes);
    }

    [Fact]
    public async Task Outputs_land_in_the_out_dir_and_the_input_and_its_folder_are_untouched()
    {
        var inDir = Dir();
        var src = CornerImage(inDir, "pic.png");
        var before = Sha(src);
        var outDir = Dir();
        var r = await Run("img-resize", new[] { src }, outDir, ("width", "20"));
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Outputs, o => Assert.Equal(outDir, IoPath.GetDirectoryName(o)));
        Assert.Equal(before, Sha(src));
        Assert.Equal(new[] { src }, Directory.GetFiles(inDir));
    }

    [Fact]
    public async Task Running_twice_into_the_same_out_dir_never_overwrites_the_first_result()
    {
        var src = CornerImage(Dir(), "pic.png");
        var outDir = Dir();
        var first = await Run("img-resize", new[] { src }, outDir, ("width", "20"));
        var second = await Run("img-resize", new[] { src }, outDir, ("width", "10"));
        Assert.NotEqual(first.Outputs[0], second.Outputs[0]);
        Assert.Equal(20, SImage.Identify(first.Outputs[0]).Width);
        Assert.Equal(10, SImage.Identify(second.Outputs[0]).Width);
    }

    [Fact]
    public async Task Unsupported_format_says_not_built_and_names_the_file()
    {
        var dir = Dir();
        var tif = IoPath.Combine(dir, "scan.tif");
        IoFile.WriteAllBytes(tif, new byte[] { 0x49, 0x49, 0x2A, 0x00, 1, 2, 3, 4 });
        var r = await Run("img-resize", new[] { tif }, Dir(), ("width", "10"));
        Assert.False(r.Ok);
        Assert.Contains("not built in this version", r.Error);
        Assert.Contains("scan.tif", r.Error);
    }

    [Fact]
    public async Task A_damaged_png_fails_loudly_and_writes_nothing()
    {
        var dir = Dir();
        var bad = IoPath.Combine(dir, "bad.png");
        IoFile.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var outDir = Dir();
        var r = await Run("img-resize", new[] { bad }, outDir, ("width", "10"));
        Assert.False(r.Ok);
        Assert.Contains("bad.png", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_run()
    {
        var src = CornerImage(Dir(), "pic.png");
        var reg = Registry();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await ToolRunner.RunAsync(reg.Find("img-resize")!,
            new ToolRequest(new[] { src }, Dir(), new Dictionary<string, string> { ["width"] = "10" }), null, cts.Token);
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
    }

    [Fact]
    public async Task Progress_is_reported_per_file_and_ends_at_100()
    {
        var files = Enumerable.Range(0, 3).Select(i => CornerImage(Dir(), "p" + i + ".png")).ToArray();
        var seen = new List<int>();
        var tool = Registry().Find("img-resize")!;
        var r = await ToolRunner.RunAsync(tool,
            new ToolRequest(files, Dir(), new Dictionary<string, string> { ["width"] = "10" }),
            new Progress<ToolProgress>(p => { lock (seen) seen.Add(p.Percent); }), CancellationToken.None);
        Assert.True(r.Ok, r.Error);
        await Task.Delay(100);
        Assert.Contains(100, seen);
    }

    // ---- crop -----------------------------------------------------------------------------------

    [Fact]
    public async Task Crop_returns_exactly_the_chosen_rectangle()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-crop", new[] { src }, Dir(), ("x", "20"), ("y", "10"), ("width", "20"), ("height", "10"));
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((20, 10), (img.Width, img.Height));
        Assert.Equal(Yellow, img[19, 9]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), img[0, 0]);
    }

    [Fact]
    public async Task Crop_outside_the_image_names_the_option()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-crop", new[] { src }, Dir(), ("x", "30"), ("y", "0"), ("width", "20"), ("height", "10"));
        Assert.False(r.Ok);
        Assert.Contains("width", r.Error);
        var missing = await Run("img-crop", new[] { src }, Dir(), ("x", "0"));
        Assert.False(missing.Ok);
        Assert.Contains("'width'", missing.Error);
    }

    // ---- rotate ---------------------------------------------------------------------------------

    [Fact]
    public async Task Rotate_right_swaps_dimensions_and_moves_the_red_corner_to_the_top_right()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-rotate", new[] { src }, Dir(), ("direction", "right"));
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((20, 40), (img.Width, img.Height));
        Assert.Equal(Red, img[19, 0]);
        Assert.Equal(Green, img[19, 39]);
        Assert.Equal(Blue, img[0, 0]);
    }

    [Fact]
    public async Task Rotate_left_moves_the_red_corner_to_the_bottom_left()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-rotate", new[] { src }, Dir(), ("direction", "left"));
        using var img = Open(r.Outputs[0]);
        Assert.Equal((20, 40), (img.Width, img.Height));
        Assert.Equal(Red, img[0, 39]);
        Assert.Equal(Yellow, img[19, 0]);
    }

    [Fact]
    public async Task Rotate_180_keeps_size_and_swaps_opposite_corners()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-rotate", new[] { src }, Dir(), ("direction", "180"));
        using var img = Open(r.Outputs[0]);
        Assert.Equal((40, 20), (img.Width, img.Height));
        Assert.Equal(Red, img[39, 19]);
        Assert.Equal(Yellow, img[0, 0]);
    }

    // ---- compress -------------------------------------------------------------------------------

    [Fact]
    public async Task Compress_jpg_makes_a_smaller_valid_jpg_of_the_same_size_and_extreme_beats_low()
    {
        var src = NoisyImage(Dir(), "photo.jpg", "jpg");
        var srcLen = new FileInfo(src).Length;
        var low = await Run("img-compress", new[] { src }, Dir(), ("level", "low"));
        var extreme = await Run("img-compress", new[] { src }, Dir(), ("level", "extreme"));
        Assert.True(low.Ok, low.Error);
        Assert.True(extreme.Ok, extreme.Error);
        Assert.True(IsJpg(low.Outputs[0]));
        Assert.True(new FileInfo(low.Outputs[0]).Length < srcLen);
        Assert.True(new FileInfo(extreme.Outputs[0]).Length < new FileInfo(low.Outputs[0]).Length);
        Assert.Equal((200, 200), (SImage.Identify(extreme.Outputs[0]).Width, SImage.Identify(extreme.Outputs[0]).Height));
    }

    [Fact]
    public async Task Compress_png_shrinks_the_file_and_keeps_the_picture_recognisable()
    {
        var src = NoisyImage(Dir(), "shot.png", "png");
        var r = await Run("img-compress", new[] { src }, Dir(), ("level", "recommended"));
        Assert.True(r.Ok, r.Error);
        Assert.True(IsPng(r.Outputs[0]));
        Assert.True(new FileInfo(r.Outputs[0]).Length < new FileInfo(src).Length);
        using var a = Open(src);
        using var b = Open(r.Outputs[0]);
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        AssertClose(a[100, 100], b[100, 100], 70, "centre");
    }

    [Fact]
    public async Task Compress_gif_shrinks_the_file()
    {
        var src = NoisyImage(Dir(), "anim.gif", "gif");
        var r = await Run("img-compress", new[] { src }, Dir(), ("level", "extreme"));
        Assert.True(r.Ok, r.Error);
        Assert.True(IsGif(r.Outputs[0]));
        Assert.True(new FileInfo(r.Outputs[0]).Length < new FileInfo(src).Length);
    }

    [Fact]
    public async Task Compress_that_cannot_shrink_the_file_fails_instead_of_returning_a_copy()
    {
        var src = SolidImage(Dir(), "tiny.png", 2, 2, Red);
        var outDir = Dir();
        var r = await Run("img-compress", new[] { src }, outDir, ("level", "low"));
        Assert.False(r.Ok);
        Assert.Contains("smaller", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public async Task Compress_bmp_is_refused_with_advice()
    {
        var src = NoisyImage(Dir(), "raw.bmp", "bmp", 40, 40);
        var r = await Run("img-compress", new[] { src }, Dir());
        Assert.False(r.Ok);
        Assert.Contains("bmp", r.Error);
    }

    // ---- to / from jpg, convert ----------------------------------------------------------------

    [Fact]
    public async Task To_jpg_flattens_transparency_onto_the_chosen_background()
    {
        var dir = Dir();
        var src = IoPath.Combine(dir, "logo.png");
        using (var img = new Image<Rgba32>(20, 20, new Rgba32(0, 0, 0, 0)))
        {
            img[10, 10] = Blue;
            img.SaveAsPng(src);
        }
        var white = await Run("img-to-jpg", new[] { src }, Dir());
        Assert.True(white.Ok, white.Error);
        Assert.True(IsJpg(white.Outputs[0]));
        using (var w = Open(white.Outputs[0]))
        {
            Assert.Equal((20, 20), (w.Width, w.Height));
            AssertClose(new Rgba32(255, 255, 255, 255), w[2, 2], 8, "transparent area");
        }
        var red = await Run("img-to-jpg", new[] { src }, Dir(), ("background", "#FF0000"), ("quality", "100"));
        using var r = Open(red.Outputs[0]);
        AssertClose(Red, r[2, 2], 12, "red background");
    }

    [Fact]
    public async Task To_jpg_quality_changes_the_file_size()
    {
        var src = NoisyImage(Dir(), "n.png", "png");
        var hi = await Run("img-to-jpg", new[] { src }, Dir(), ("quality", "95"));
        var lo = await Run("img-to-jpg", new[] { src }, Dir(), ("quality", "20"));
        Assert.True(new FileInfo(lo.Outputs[0]).Length < new FileInfo(hi.Outputs[0]).Length);
    }

    [Fact]
    public async Task To_jpg_rejects_a_transparent_background_colour()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-to-jpg", new[] { src }, Dir(), ("background", "#80FF0000"));
        Assert.False(r.Ok);
        Assert.Contains("background", r.Error);
    }

    [Fact]
    public async Task From_jpg_makes_png_gif_and_bmp_with_the_right_signatures_and_the_same_size()
    {
        var src = NoisyImage(Dir(), "photo.jpg", "jpg", 64, 48);
        foreach (var (fmt, check) in new (string, Func<string, bool>)[] { ("png", IsPng), ("gif", IsGif), ("bmp", IsBmp) })
        {
            var r = await Run("img-from-jpg", new[] { src }, Dir(), ("format", fmt));
            Assert.True(r.Ok, r.Error);
            Assert.True(check(r.Outputs[0]), fmt);
            Assert.Equal((64, 48), (SImage.Identify(r.Outputs[0]).Width, SImage.Identify(r.Outputs[0]).Height));
        }
    }

    [Fact]
    public async Task From_jpg_png_keeps_the_pixels_of_the_decoded_jpg()
    {
        var src = NoisyImage(Dir(), "photo.jpg", "jpg", 32, 32);
        var r = await Run("img-from-jpg", new[] { src }, Dir());
        using var a = Open(src);
        using var b = Open(r.Outputs[0]);
        for (var y = 0; y < 32; y += 7)
            for (var x = 0; x < 32; x += 5)
                Assert.Equal(a[x, y], b[x, y]);
    }

    [Fact]
    public async Task From_jpg_refuses_a_png_wearing_a_jpg_name()
    {
        var dir = Dir();
        var real = CornerImage(dir, "x.png");
        var fake = IoPath.Combine(dir, "fake.jpg");
        IoFile.Copy(real, fake);
        var r = await Run("img-from-jpg", new[] { fake }, Dir());
        Assert.False(r.Ok);
        Assert.Contains("not a JPG", r.Error);
    }

    [Fact]
    public async Task Convert_any_converts_between_formats_and_refuses_a_no_op()
    {
        var png = CornerImage(Dir(), "pic.png");
        var bmp = await Run("img-convert-any", new[] { png }, Dir(), ("to", "bmp"));
        Assert.True(bmp.Ok, bmp.Error);
        Assert.True(IsBmp(bmp.Outputs[0]));
        using (var b = Open(bmp.Outputs[0])) Assert.Equal(Red, b[0, 0]);

        var jpg = await Run("img-convert-any", new[] { png }, Dir(), ("to", "jpg"));
        Assert.True(IsJpg(jpg.Outputs[0]));

        var gif = await Run("img-convert-any", new[] { bmp.Outputs[0] }, Dir(), ("to", "gif"));
        Assert.True(gif.Ok, gif.Error);
        Assert.True(IsGif(gif.Outputs[0]));

        var same = await Run("img-convert-any", new[] { png }, Dir(), ("to", "png"));
        Assert.False(same.Ok);
        Assert.Contains("already png", same.Error);
    }

    // ---- watermark ------------------------------------------------------------------------------

    [Fact]
    public async Task Watermark_stamps_the_image_at_the_chosen_corner_and_leaves_the_rest_alone()
    {
        var dir = Dir();
        var baseImg = SolidImage(dir, "base.png", 100, 100, new Rgba32(255, 255, 255, 255));
        var mark = SolidImage(dir, "mark.png", 10, 10, new Rgba32(0, 0, 0, 255));
        var r = await Run("img-watermark", new[] { baseImg }, Dir(),
            ("image", mark), ("position", "top-left"), ("opacity", "100"), ("scale", "20"));
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((100, 100), (img.Width, img.Height));
        AssertClose(new Rgba32(0, 0, 0, 255), img[5, 5], 5, "inside the mark");
        AssertClose(new Rgba32(255, 255, 255, 255), img[60, 60], 0, "away from the mark");
        AssertClose(new Rgba32(255, 255, 255, 255), img[0, 0], 0, "margin");
    }

    [Fact]
    public async Task Watermark_opacity_blends_towards_the_base()
    {
        var dir = Dir();
        var baseImg = SolidImage(dir, "base.png", 100, 100, new Rgba32(255, 255, 255, 255));
        var mark = SolidImage(dir, "mark.png", 10, 10, new Rgba32(0, 0, 0, 255));
        var r = await Run("img-watermark", new[] { baseImg }, Dir(), ("image", mark), ("position", "center"), ("opacity", "50"));
        using var img = Open(r.Outputs[0]);
        var c = img[50, 50];
        Assert.InRange(c.R, 100, 155);
    }

    [Fact]
    public async Task Watermark_position_bottom_right_moves_the_stamp()
    {
        var dir = Dir();
        var baseImg = SolidImage(dir, "base.png", 100, 100, new Rgba32(255, 255, 255, 255));
        var mark = SolidImage(dir, "mark.png", 10, 10, new Rgba32(0, 0, 0, 255));
        var r = await Run("img-watermark", new[] { baseImg }, Dir(),
            ("image", mark), ("position", "bottom-right"), ("opacity", "100"), ("scale", "20"));
        using var img = Open(r.Outputs[0]);
        AssertClose(new Rgba32(0, 0, 0, 255), img[93, 93], 5, "bottom-right");
        AssertClose(new Rgba32(255, 255, 255, 255), img[5, 5], 0, "top-left");
    }

    [Fact]
    public async Task Watermark_needs_text_or_image()
    {
        var src = CornerImage(Dir(), "pic.png");
        var none = await Run("img-watermark", new[] { src }, Dir());
        Assert.False(none.Ok);
        Assert.Contains("'image'", none.Error);
    }

    // ---- dpi ------------------------------------------------------------------------------------

    [Fact]
    public async Task Dpi_writes_the_new_density_into_a_png_without_touching_pixels()
    {
        var src = CornerImage(Dir(), "pic.png");
        var r = await Run("img-dpi", new[] { src }, Dir(), ("dpi", "150"));
        Assert.True(r.Ok, r.Error);
        var info = SImage.Identify(r.Outputs[0]);
        Assert.InRange(Dpi(info).H, 149.9, 150.1);
        Assert.InRange(Dpi(info).V, 149.9, 150.1);
        using var a = Open(src);
        using var b = Open(r.Outputs[0]);
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                Assert.Equal(a[x, y], b[x, y]);
    }

    [Fact]
    public async Task Dpi_writes_the_density_into_a_jpg_and_a_bmp_and_refuses_a_gif()
    {
        var jpg = NoisyImage(Dir(), "p.jpg", "jpg", 40, 40);
        var rj = await Run("img-dpi", new[] { jpg }, Dir(), ("dpi", "300"));
        Assert.True(rj.Ok, rj.Error);
        Assert.True(IsJpg(rj.Outputs[0]));
        Assert.InRange(Dpi(SImage.Identify(rj.Outputs[0])).H, 299, 301);

        var bmp = NoisyImage(Dir(), "p.bmp", "bmp", 40, 40);
        var rb = await Run("img-dpi", new[] { bmp }, Dir(), ("dpi", "96"));
        Assert.True(rb.Ok, rb.Error);
        Assert.InRange(Dpi(SImage.Identify(rb.Outputs[0])).H, 95, 97);

        var gif = NoisyImage(Dir(), "p.gif", "gif", 40, 40);
        var rg = await Run("img-dpi", new[] { gif }, Dir());
        Assert.False(rg.Ok);
        Assert.Contains("gif", rg.Error);
    }

    // ---- option validation at the boundary -----------------------------------------------------

    [Fact]
    public async Task Bad_option_values_are_rejected_naming_the_option_before_any_work()
    {
        var src = CornerImage(Dir(), "pic.png");
        var outDir = Dir();
        var a = await Run("img-resize", new[] { src }, outDir, ("width", "0"));
        Assert.Equal(ToolOutcome.Invalid, a.Outcome);
        Assert.Contains("width", a.Error);
        var b = await Run("img-rotate", new[] { src }, outDir, ("direction", "sideways"));
        Assert.Equal(ToolOutcome.Invalid, b.Outcome);
        Assert.Contains("direction", b.Error);
        var c = await Run("img-dpi", new[] { src }, outDir, ("dpi", "99999"));
        Assert.Equal(ToolOutcome.Invalid, c.Outcome);
        Assert.Contains("dpi", c.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    // ---- geometry maths -------------------------------------------------------------------------

    [Theory]
    [InlineData(40, 20, 20, 0, 20, 10)]
    [InlineData(40, 20, 0, 40, 80, 40)]
    [InlineData(40, 20, 100, 100, 100, 50)]
    public void Resize_target_keeps_the_ratio(int sw, int sh, int w, int h, int ew, int eh)
    {
        Assert.Equal((ew, eh), ImageGeometry.ResizeTarget(sw, sh, "pixels", w, h, 50, true));
    }

    [Fact]
    public void Rotate_point_maths_matches_the_clockwise_definition()
    {
        Assert.Equal((19, 0, 20, 40), ImageGeometry.RotatePoint(40, 20, 0, 0, 90));
        Assert.Equal((0, 39, 20, 40), ImageGeometry.RotatePoint(40, 20, 0, 0, 270));
        Assert.Equal((39, 19, 40, 20), ImageGeometry.RotatePoint(40, 20, 0, 0, 180));
    }
}
