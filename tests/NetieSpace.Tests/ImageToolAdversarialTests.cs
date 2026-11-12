using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

public class ImageToolAdversarialTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();

    [Fact]
    public async Task Convert_any_jpg_to_jpg_is_refused_like_every_other_same_format()
    {
        var d = Dir();
        var src = NoisyImage(d, "a.jpg", "jpg");
        var r = await Run("img-convert-any", new[] { src }, IoPath.Combine(d, "out"), ("to", "jpg"));
        Assert.False(r.Ok);
        Assert.Contains("already", r.Error);
    }

    [Fact]
    public async Task Exif_orientation_is_baked_in_by_resize()
    {
        var d = Dir();
        var p = IoPath.Combine(d, "o.jpg");
        using (var img = new Image<Rgba32>(40, 20, new Rgba32(255, 255, 255, 255)))
        {
            img.Metadata.ExifProfile = new ExifProfile();
            img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            img.Save(p, new JpegEncoder { Quality = 95 });
        }
        var r = await Run("img-resize", new[] { p }, IoPath.Combine(d, "out"), ("mode", "percentage"), ("percent", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        Assert.Equal((20, 40), (o.Width, o.Height));
    }

    [Fact]
    public async Task To_jpg_flattens_semi_and_fully_transparent_pixels_onto_background()
    {
        var d = Dir();
        var p = IoPath.Combine(d, "t.png");
        using (var img = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0)))
        {
            for (var y = 0; y < 8; y++) for (var x = 0; x < 16; x++) img[x, y] = new Rgba32(255, 0, 0, 128);
            img.Save(p, new PngEncoder());
        }
        var r = await Run("img-to-jpg", new[] { p }, IoPath.Combine(d, "out"), ("background", "#FFFFFF"), ("quality", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        AssertClose(new Rgba32(255, 255, 255, 255), o[8, 12], 6, "transparent area");
        AssertClose(new Rgba32(255, 127, 127, 255), o[8, 3], 10, "half red over white");
    }

    [Fact]
    public async Task To_jpg_uses_chosen_background_for_transparent_png()
    {
        var d = Dir();
        var p = IoPath.Combine(d, "t.png");
        using (var img = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0))) img.Save(p, new PngEncoder());
        var r = await Run("img-to-jpg", new[] { p }, IoPath.Combine(d, "out"), ("background", "#0000FF"), ("quality", "100"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        AssertClose(new Rgba32(0, 0, 255, 255), o[5, 5], 8, "blue background");
    }

    [Theory]
    [InlineData("zero.png", 0)]
    [InlineData("junk.jpg", 1)]
    [InlineData("trunc.png", 2)]
    [InlineData("trunc.jpg", 3)]
    public async Task Corrupt_inputs_fail_clearly_for_every_tool(string name, int kind)
    {
        var d = Dir();
        var good = NoisyImage(d, "g.png", "png");
        var goodJ = NoisyImage(d, "g.jpg", "jpg");
        var path = IoPath.Combine(d, name);
        switch (kind)
        {
            case 0: IoFile.WriteAllBytes(path, Array.Empty<byte>()); break;
            case 1: IoFile.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("not an image at all")); break;
            case 2: IoFile.WriteAllBytes(path, IoFile.ReadAllBytes(good).Take(300).ToArray()); break;
            default: IoFile.WriteAllBytes(path, IoFile.ReadAllBytes(goodJ).Take(400).ToArray()); break;
        }
        foreach (var (id, opts) in new (string, (string, string)[])[]
        {
            ("img-resize", new[] { ("width", "10") }),
            ("img-rotate", Array.Empty<(string, string)>()),
            ("img-compress", Array.Empty<(string, string)>()),
            ("img-to-jpg", Array.Empty<(string, string)>()),
            ("img-dpi", Array.Empty<(string, string)>()),
            ("img-crop", new[] { ("width", "2"), ("height", "2") }),
        })
        {
            var out1 = IoPath.Combine(d, "out-" + id);
            var r = await Run(id, new[] { path }, out1, opts);
            Assert.False(r.Ok, id + " accepted a corrupt file");
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            if (IoDirectory.Exists(out1)) Assert.Empty(IoDirectory.GetFiles(out1));
        }
    }

    [Fact]
    public async Task Non_ascii_apostrophe_names_and_same_names_from_two_folders_never_collide()
    {
        var d = Dir();
        var a = CornerImage(IoPath.Combine(d, "one"), "Zoe's  photo \u00E9\u4E2D (1).png", 30, 10);
        var b = CornerImage(IoPath.Combine(d, "two"), "Zoe's  photo \u00E9\u4E2D (1).png", 50, 10);
        var r = await Run("img-resize", new[] { a, b }, IoPath.Combine(d, "new", "deep"), ("mode", "percentage"), ("percent", "200"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Outputs.Distinct().Count());
        using var o1 = Open(r.Outputs[0]);
        using var o2 = Open(r.Outputs[1]);
        Assert.Equal(60, o1.Width);
        Assert.Equal(100, o2.Width);
    }

    [Fact]
    public async Task Odd_dimensions_rotate_swap_and_crop_edges_work()
    {
        var d = Dir();
        var p = CornerImage(d, "odd.png", 7, 3);
        var r = await Run("img-rotate", new[] { p }, IoPath.Combine(d, "o"), ("direction", "left"));
        Assert.True(r.Ok, r.Error);
        using (var o = Open(r.Outputs[0]))
        {
            Assert.Equal((3, 7), (o.Width, o.Height));
            Assert.Equal(Green, o[0, 0]);
            Assert.Equal(Red, o[0, 6]);
        }
        var c = await Run("img-crop", new[] { p }, IoPath.Combine(d, "o"), ("x", "6"), ("y", "2"), ("width", "1"), ("height", "1"));
        Assert.True(c.Ok, c.Error);
        using var oc = Open(c.Outputs[0]);
        Assert.Equal(Yellow, oc[0, 0]);
        var bad = await Run("img-crop", new[] { p }, IoPath.Combine(d, "o"), ("x", "6"), ("y", "2"), ("width", "2"), ("height", "1"));
        Assert.False(bad.Ok);
        Assert.Contains("width", bad.Error);
    }

    [Fact]
    public async Task Resize_extreme_ratio_never_makes_a_zero_side_and_keep_ratio_false_needs_both()
    {
        var d = Dir();
        var p = SolidImage(d, "wide.png", 1000, 2, Red);
        var r = await Run("img-resize", new[] { p }, IoPath.Combine(d, "o"), ("width", "10"));
        Assert.True(r.Ok, r.Error);
        using (var o = Open(r.Outputs[0])) Assert.Equal((10, 1), (o.Width, o.Height));
        var bad = await Run("img-resize", new[] { p }, IoPath.Combine(d, "o"), ("width", "10"), ("keep_ratio", "false"));
        Assert.False(bad.Ok);
        Assert.Contains("height", bad.Error);
        var stretch = await Run("img-resize", new[] { p }, IoPath.Combine(d, "o"), ("width", "10"), ("height", "10"), ("keep_ratio", "false"));
        using var os = Open(stretch.Outputs[0]);
        Assert.Equal((10, 10), (os.Width, os.Height));
    }

    [Fact]
    public async Task Resize_of_30000_by_30000_is_refused_for_size_not_attempted()
    {
        var d = Dir();
        var p = SolidImage(d, "s.png", 10, 10, Red);
        var r = await Run("img-resize", new[] { p }, IoPath.Combine(d, "o"), ("width", "30000"), ("height", "30000"), ("keep_ratio", "false"));
        Assert.False(r.Ok);
        Assert.Contains("limit", r.Error);
    }

    [Fact]
    public async Task Animated_gif_keeps_all_frames_through_resize_and_rotate()
    {
        var d = Dir();
        var p = IoPath.Combine(d, "anim.gif");
        using (var g = new Image<Rgba32>(20, 10, Red))
        {
            for (var i = 0; i < 2; i++)
            {
                using var f = new Image<Rgba32>(20, 10, i == 0 ? Green : Blue);
                g.Frames.AddFrame(f.Frames.RootFrame);
            }
            g.Save(p, new GifEncoder());
        }
        foreach (var (id, opt) in new[] { ("img-resize", ("width", "40")), ("img-rotate", ("direction", "right")) })
        {
            var r = await Run(id, new[] { p }, IoPath.Combine(d, "o" + id), opt);
            Assert.True(r.Ok, r.Error);
            using var o = SImage.Load(r.Outputs[0]);
            Assert.Equal(3, o.Frames.Count);
        }
    }

    [Fact]
    public async Task Watermark_lands_in_the_chosen_corner_and_not_elsewhere_and_text_is_refused()
    {
        var d = Dir();
        var baseImg = SolidImage(d, "b.png", 200, 100, new Rgba32(255, 255, 255, 255));
        var mark = SolidImage(d, "m.png", 20, 20, new Rgba32(0, 0, 0, 255));
        var r = await Run("img-watermark", new[] { baseImg }, IoPath.Combine(d, "o"),
            ("image", mark), ("opacity", "100"), ("scale", "10"), ("position", "bottom-right"));
        Assert.True(r.Ok, r.Error);
        using (var o = Open(r.Outputs[0]))
        {
            Assert.Equal(new Rgba32(0, 0, 0, 255), o[185, 90]);
            Assert.Equal(new Rgba32(255, 255, 255, 255), o[5, 5]);
            Assert.Equal(new Rgba32(255, 255, 255, 255), o[100, 50]);
        }
        var t = await Run("img-watermark", new[] { baseImg }, IoPath.Combine(d, "o"), ("text", "a'b:c%d\\e/f,\"g"));
        Assert.False(t.Ok);
        var oversize = SolidImage(d, "huge.png", 900, 900, new Rgba32(0, 0, 0, 255));
        var r2 = await Run("img-watermark", new[] { baseImg }, IoPath.Combine(d, "o"), ("image", oversize), ("scale", "100"), ("opacity", "100"));
        Assert.True(r2.Ok, r2.Error);
        using var o2 = Open(r2.Outputs[0]);
        Assert.Equal((200, 100), (o2.Width, o2.Height));
    }

    [Fact]
    public async Task Watermark_at_partial_opacity_blends_rather_than_replacing()
    {
        var d = Dir();
        var baseImg = SolidImage(d, "b.png", 100, 100, new Rgba32(255, 255, 255, 255));
        var mark = SolidImage(d, "m.png", 50, 50, new Rgba32(0, 0, 0, 255));
        var r = await Run("img-watermark", new[] { baseImg }, IoPath.Combine(d, "o"), ("image", mark), ("opacity", "50"), ("scale", "50"));
        Assert.True(r.Ok, r.Error);
        using var o = Open(r.Outputs[0]);
        AssertClose(new Rgba32(128, 128, 128, 255), o[50, 50], 6, "blend");
    }

    [Theory]
    [InlineData("png", 1)]
    [InlineData("png", 2400)]
    [InlineData("jpg", 96)]
    [InlineData("bmp", 150)]
    public async Task Dpi_reads_back_and_size_survives(string fmt, int dpi)
    {
        var d = Dir();
        var p = CornerImage(d, "x." + fmt, 40, 20, fmt);
        var r = await Run("img-dpi", new[] { p }, IoPath.Combine(d, "o"), ("dpi", dpi.ToString()));
        Assert.True(r.Ok, r.Error);
        var info = SImage.Identify(r.Outputs[0]);
        var (h, v) = Dpi(info);
        Assert.InRange(h, dpi - 1.0, dpi + 1.0);
        Assert.InRange(v, dpi - 1.0, dpi + 1.0);
        Assert.Equal((40, 20), (info.Width, info.Height));
    }

    [Fact]
    public async Task Compress_never_returns_a_file_that_grew_for_any_level_and_format()
    {
        var d = Dir();
        foreach (var fmt in new[] { "jpg", "png", "gif" })
        {
            var p = NoisyImage(d, "n." + fmt, fmt);
            var before = new System.IO.FileInfo(p).Length;
            foreach (var level in new[] { "low", "recommended", "extreme" })
            {
                var r = await Run("img-compress", new[] { p }, IoPath.Combine(d, "o" + fmt + level), ("level", level));
                if (r.Ok)
                {
                    Assert.True(new System.IO.FileInfo(r.Outputs[0]).Length < before, fmt + level);
                    using var o = Open(r.Outputs[0]);
                    Assert.Equal((200, 200), (o.Width, o.Height));
                }
                else Assert.False(string.IsNullOrWhiteSpace(r.Error));
            }
        }
    }

    [Fact]
    public async Task Cancelled_run_reports_cancelled()
    {
        var d = Dir();
        var p = CornerImage(d, "c.png");
        var reg = Registry();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await ToolRunner.RunAsync(reg.Find("img-rotate")!,
            new ToolRequest(new[] { p }, IoPath.Combine(d, "o"), new Dictionary<string, string>()), null, cts.Token);
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
    }

    [Fact]
    public async Task Content_not_extension_decides_format_and_input_is_untouched()
    {
        var d = Dir();
        var p = NoisyImage(d, "lie.PNG", "jpg");
        var sha = Sha(p);
        var r = await Run("img-rotate", new[] { p }, IoPath.Combine(d, "o"));
        Assert.True(r.Ok, r.Error);
        Assert.True(IsJpg(r.Outputs[0]));
        Assert.EndsWith(".jpg", r.Outputs[0]);
        Assert.Equal(sha, Sha(p));
    }

    [Fact]
    public async Task Output_dir_equal_to_input_dir_does_not_overwrite_input()
    {
        var d = Dir();
        var p = CornerImage(d, "same.png");
        var sha = Sha(p);
        var r1 = await Run("img-rotate", new[] { p }, d);
        var r2 = await Run("img-rotate", new[] { p }, d);
        Assert.True(r1.Ok && r2.Ok);
        Assert.NotEqual(r1.Outputs[0], r2.Outputs[0]);
        Assert.Equal(sha, Sha(p));
    }

    [Fact]
    public async Task From_jpg_rejects_png_renamed_jpg_and_converts_real_jpg_to_each_format()
    {
        var d = Dir();
        var png = CornerImage(d, "fake.png");
        IoFile.Copy(png, IoPath.Combine(d, "fake.jpg"));
        var bad = await Run("img-from-jpg", new[] { IoPath.Combine(d, "fake.jpg") }, IoPath.Combine(d, "o"));
        Assert.False(bad.Ok);
        var real = NoisyImage(d, "real.jpg", "jpg");
        foreach (var f in new[] { "png", "gif", "bmp" })
        {
            var r = await Run("img-from-jpg", new[] { real }, IoPath.Combine(d, "o" + f), ("format", f));
            Assert.True(r.Ok, r.Error);
            using var o = Open(r.Outputs[0]);
            Assert.Equal((200, 200), (o.Width, o.Height));
        }
    }
}
