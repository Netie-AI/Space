using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

/// <summary>Audit of the image runners: placement, sizes, orientation, alpha, animation, colour profile and odd shapes.</summary>
public class AuditImageTests
{
    private static readonly ToolRegistry Registry = ImageToolTestSupport.Registry();

    private static ToolResult Run(string id, string[] files, params (string, string)[] opts)
    {
        var tool = Registry.Find(id)!;
        var o = ToolTestSupport.NewTempDir();
        return ToolRunner.RunAsync(tool, new ToolRequest(files, o, opts.ToDictionary(x => x.Item1, x => x.Item2)), null, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string RunOk(string id, string[] files, params (string, string)[] opts)
    {
        var r = Run(id, files, opts);
        Assert.True(r.Ok, id + ": " + r.Error);
        return r.Outputs[0];
    }

    // sRGB profile, 588 bytes, as a real camera or editor would embed it
    private const string SrgbIcc = 
        "AAACTGxjbXMEQAAAbW50clJHQiBYWVogB+oACwAMABcAOQAZYWNzcEFQUEwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1sY21zAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALZGVzYwAAAQgAAAA2Y3BydAAAAUAAAABMd3RwdAAAAYwAAAAUY2hhZAAAAaAAAAAsclhZWgAAAcwAAAAUYlhZWgAAAeAAAAAUZ1hZWgAAAfQAAAAUclRSQwAAAggAAAAgZ1RSQwAAAggAAAAgYlRSQwAAAggAAAAgY2hybQAAAigAAAAkbWx1YwAAAAAAAAABAAAADGVuVVMAAAAaAAAAHABzAFIARwBCACAAYgB1AGkAbAB0AC0AaQBuAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAADAAAAAcAE4AbwAgAGMAbwBwAHkAcgBpAGcAaAB0ACwAIAB1AHMAZQAgAGYAcgBlAGUAbAB5WFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDEIAAAXe///zJQAAB5MAAP2Q///7of///aIAAAPcAADAblhZWiAAAAAAAABvoAAAOPUAAAOQWFlaIAAAAAAAACSfAAAPhAAAtsNYWVogAAAAAAAAYpcAALeHAAAY2XBhcmEAAAAAAAMAAAACZmYAAPKnAAANWQAAE9AAAApbY2hybQAAAAAAAwAAAACj1wAAVHsAAEzNAACZmgAAJmYAAA9c";

    // ---------------- grid positions ----------------

    private static (int Left, int Top, int Right, int Bottom)? Box(Image<Rgba32> img, Func<Rgba32, bool> hit)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (hit(img[x, y])) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y); }
        return r < 0 ? null : (l, t, r, b);
    }

    private static readonly (string Name, int Col, int Row)[] Cells =
    {
        ("top-left", 0, 0), ("top-center", 1, 0), ("top-right", 2, 0),
        ("middle-left", 0, 1), ("middle-center", 1, 1), ("middle-right", 2, 1),
        ("bottom-left", 0, 2), ("bottom-center", 1, 2), ("bottom-right", 2, 2),
        ("center", 1, 1), ("north", 1, 0), ("south", 1, 2), ("east", 2, 1), ("west", 0, 1),
        ("northeast", 2, 0), ("northwest", 0, 0), ("southeast", 2, 2), ("southwest", 0, 2),
    };

    [Fact]
    public void An_image_mark_lands_in_the_cell_it_was_asked_for_at_all_nine_positions()
    {
        var dir = ToolTestSupport.NewTempDir();
        var basePng = ImageToolTestSupport.SolidImage(dir, "base.png", 300, 200, new Rgba32(255, 255, 255, 255));
        var mark = ImageToolTestSupport.SolidImage(dir, "mark.png", 20, 20, new Rgba32(255, 0, 0, 255));
        var bad = new List<string>();
        foreach (var (name, col, row) in Cells)
        {
            var o = RunOk("img-watermark", new[] { basePng }, ("image", mark), ("position", name), ("opacity", "100"), ("scale", "10"));
            using var img = SImage.Load<Rgba32>(o);
            var box = Box(img, p => p.R > 200 && p.G < 60 && p.B < 60);
            if (box is null) { bad.Add(name + ": no mark found"); continue; }
            var cx = (box.Value.Left + box.Value.Right) / 2.0;
            var cy = (box.Value.Top + box.Value.Bottom) / 2.0;
            if ((int)(cx / (img.Width / 3.0)) != col || (int)(cy / (img.Height / 3.0)) != row)
                bad.Add($"{name}: mark centre at {cx:0},{cy:0} is not in column {col} row {row}");
            if (box.Value.Left < 0 || box.Value.Right >= img.Width || box.Value.Top < 0 || box.Value.Bottom >= img.Height)
                bad.Add(name + ": mark leaves the image");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void A_text_mark_lands_in_the_cell_it_was_asked_for_at_all_nine_positions()
    {
        var dir = ToolTestSupport.NewTempDir();
        var basePng = ImageToolTestSupport.SolidImage(dir, "base.png", 360, 240, new Rgba32(255, 255, 255, 255));
        var bad = new List<string>();
        foreach (var (name, col, row) in Cells.Take(9))
        {
            var o = RunOk("img-watermark", new[] { basePng }, ("text", "MARK"), ("position", name), ("opacity", "100"), ("scale", "20"), ("color", "#FF0000"));
            using var img = SImage.Load<Rgba32>(o);
            var box = Box(img, p => p.R > 200 && p.G < 90 && p.B < 90);
            if (box is null) { bad.Add(name + ": no text found"); continue; }
            var cx = (box.Value.Left + box.Value.Right) / 2.0;
            var cy = (box.Value.Top + box.Value.Bottom) / 2.0;
            if ((int)(cx / (img.Width / 3.0)) != col || (int)(cy / (img.Height / 3.0)) != row)
                bad.Add($"{name}: text centre at {cx:0},{cy:0} is not in column {col} row {row}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    // ---------------- percent, zero and negative sizes ----------------

    [Theory]
    [InlineData(1, 200, 100, 2, 1)]
    [InlineData(1, 1, 1, 1, 1)]
    [InlineData(100, 200, 100, 200, 100)]
    [InlineData(100, 1, 1, 1, 1)]
    [InlineData(400, 10, 5, 40, 20)]
    [InlineData(50, 3, 3, 2, 2)]
    public void Percentage_resize_gives_the_size_a_person_expects(int percent, int w, int h, int ew, int eh)
    {
        var src = ImageToolTestSupport.NoisyImage(ToolTestSupport.NewTempDir(), "a.png", "png", w, h);
        var o = RunOk("img-resize", new[] { src }, ("mode", "percentage"), ("percent", percent.ToString()));
        var info = SImage.Identify(o);
        Assert.Equal((ew, eh), (info.Width, info.Height));
    }

    [Theory]
    [InlineData("percent", "0")]
    [InlineData("percent", "-10")]
    [InlineData("percent", "401")]
    [InlineData("width", "0")]
    [InlineData("width", "-5")]
    [InlineData("height", "-1")]
    [InlineData("width", "abc")]
    public void Zero_and_negative_numbers_are_refused_naming_the_option(string key, string value)
    {
        var src = ImageToolTestSupport.NoisyImage(ToolTestSupport.NewTempDir(), "a.png", "png", 40, 20);
        var opts = new List<(string, string)> { (key, value) };
        if (key == "percent") opts.Add(("mode", "percentage")); else opts.Add(key == "width" ? ("height", "10") : ("width", "10"));
        var r = Run("img-resize", new[] { src }, opts.ToArray());
        Assert.False(r.Ok);
        Assert.Contains("'" + key + "'", r.Error!);
    }

    [Fact]
    public void A_crop_outside_the_image_is_refused_naming_the_option_and_one_inside_is_exact()
    {
        var src = ImageToolTestSupport.CornerImage(ToolTestSupport.NewTempDir(), "c.png", 40, 20);
        foreach (var (k, v) in new[] { ("x", "40"), ("y", "20"), ("x", "-1"), ("width", "0") })
        {
            var o = new List<(string, string)> { ("width", "10"), ("height", "10"), (k, v) };
            var r = Run("img-crop", new[] { src }, o.GroupBy(p => p.Item1).Select(g => g.Last()).ToArray());
            Assert.False(r.Ok, k + "=" + v);
            Assert.Contains("'", r.Error!);
        }
        var okPath = RunOk("img-crop", new[] { src }, ("x", "39"), ("y", "19"), ("width", "1"), ("height", "1"));
        using var one = SImage.Load<Rgba32>(okPath);
        Assert.Equal((1, 1), (one.Width, one.Height));
        ImageToolTestSupport.AssertClose(ImageToolTestSupport.Yellow, one[0, 0], 2, "bottom-right pixel");
    }

    // ---------------- EXIF orientation ----------------

    private static string OrientedJpeg(string dir, ushort orientation)
    {
        // 40 wide, 20 high as stored; red top-left, green top-right, blue bottom-left, yellow bottom-right
        var path = IoPath.Combine(dir, "photo.jpg");
        using var img = new Image<Rgba32>(40, 20, new Rgba32(255, 255, 255, 255));
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                img[x, y] = ImageToolTestSupport.Red;
                img[39 - x, y] = ImageToolTestSupport.Green;
                img[x, 19 - y] = ImageToolTestSupport.Blue;
                img[39 - x, 19 - y] = ImageToolTestSupport.Yellow;
            }
        img.Metadata.ExifProfile = new ExifProfile();
        img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        img.Save(path, new JpegEncoder { Quality = 100 });
        return path;
    }

    [Theory]
    [InlineData("img-resize", "mode=percentage,percent=100")]
    [InlineData("img-rotate", "direction=180")]
    [InlineData("img-dpi", "dpi=300")]
    [InlineData("img-from-jpg", "format=png")]
    [InlineData("img-convert-any", "to=png")]
    [InlineData("img-to-jpg", "quality=100")]
    [InlineData("img-compress", "level=low")]
    public void A_photo_with_an_orientation_tag_comes_out_the_way_a_viewer_showed_it(string id, string optText)
    {
        var dir = ToolTestSupport.NewTempDir();
        var photo = OrientedJpeg(dir, 6); // stored 40x20, shown 20x40 (turned 90 clockwise)
        var opts = optText.Split(',').Select(p => (p.Split('=')[0], p.Split('=')[1])).ToArray();
        var r = Run(id, new[] { photo }, opts);
        if (!r.Ok && id == "img-compress") return; // a refusal ("already compact") is not a wrong picture
        Assert.True(r.Ok, id + ": " + r.Error);
        using var img = SImage.Load<Rgba32>(r.Outputs[0]);
        var turned = id == "img-rotate";
        // shown 20 wide and 40 high; rotate adds a half turn on top
        Assert.Equal((20, 40), (img.Width, img.Height));
        Assert.True(img.Metadata.ExifProfile?.GetValue(ExifTag.Orientation)?.Value is null or 1, id + " left an orientation tag on already turned pixels");
        // stored top-left red, after a clockwise quarter turn it is the top-RIGHT of the shown picture
        var topRight = turned ? img[0, 39] : img[19, 0];
        var topLeft = turned ? img[19, 39] : img[0, 0];
        ImageToolTestSupport.AssertClose(ImageToolTestSupport.Red, topRight, 40, id + " stored-red corner");
        ImageToolTestSupport.AssertClose(ImageToolTestSupport.Blue, topLeft, 40, id + " stored-blue corner");
    }

    // ---------------- alpha ----------------

    private static string TransparentPng(string dir)
    {
        var path = IoPath.Combine(dir, "logo.png");
        using var img = new Image<Rgba32>(40, 40, new Rgba32(0, 0, 0, 0));
        for (var y = 10; y < 30; y++)
            for (var x = 10; x < 30; x++) img[x, y] = new Rgba32(200, 30, 30, 255);
        for (var x = 0; x < 40; x++) img[x, 38] = new Rgba32(30, 30, 200, 128);
        img.Save(path, new PngEncoder());
        return path;
    }

    [Theory]
    [InlineData("img-resize", "mode=percentage,percent=100")]
    [InlineData("img-crop", "width=40,height=40")]
    [InlineData("img-rotate", "direction=right")]
    [InlineData("img-dpi", "dpi=150")]
    [InlineData("img-compress", "level=low")]
    [InlineData("img-compress", "level=recommended")]
    public void Transparent_pixels_stay_transparent_in_tools_that_write_png(string id, string optText)
    {
        var logo = TransparentPng(ToolTestSupport.NewTempDir());
        var opts = optText.Split(',').Select(p => (p.Split('=')[0], p.Split('=')[1])).ToArray();
        var r = Run(id, new[] { logo }, opts);
        if (!r.Ok && id == "img-compress") return;
        Assert.True(r.Ok, id + ": " + r.Error);
        using var img = SImage.Load<Rgba32>(r.Outputs[0]);
        var rotated = id == "img-rotate";
        Assert.Equal(0, img[5, 5].A);
        Assert.Equal(255, img[20, 20].A);
        // the half-transparent line stays half transparent (rotated right it becomes a column on the left)
        var half = rotated ? img[1, 20] : img[20, 38];
        Assert.InRange((int)half.A, 100, 156);
    }

    [Fact]
    public void A_transparent_png_made_into_a_jpg_gets_the_background_not_black()
    {
        var logo = TransparentPng(ToolTestSupport.NewTempDir());
        using (var white = SImage.Load<Rgba32>(RunOk("img-to-jpg", new[] { logo })))
            ImageToolTestSupport.AssertClose(new Rgba32(255, 255, 255, 255), white[5, 5], 16, "default background");
        using (var green = SImage.Load<Rgba32>(RunOk("img-to-jpg", new[] { logo }, ("background", "#00FF00"))))
            ImageToolTestSupport.AssertClose(new Rgba32(0, 255, 0, 255), green[5, 5], 16, "chosen background");
        using (var conv = SImage.Load<Rgba32>(RunOk("img-convert-any", new[] { logo }, ("to", "jpg"))))
            ImageToolTestSupport.AssertClose(new Rgba32(255, 255, 255, 255), conv[5, 5], 16, "convert-any background");
    }

    // ---------------- animated gif ----------------

    private static string AnimatedGif(string dir, int frames = 3)
    {
        var path = IoPath.Combine(dir, "anim.gif");
        var colours = new[] { ImageToolTestSupport.Red, ImageToolTestSupport.Green, ImageToolTestSupport.Blue, ImageToolTestSupport.Yellow };
        using var img = new Image<Rgba32>(32, 16, colours[0]);
        img.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 7;
        for (var i = 1; i < frames; i++)
        {
            using var f = new Image<Rgba32>(32, 16, colours[i % colours.Length]);
            f.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 7 + i * 3;
            img.Frames.AddFrame(f.Frames.RootFrame);
        }
        img.Metadata.GetGifMetadata().RepeatCount = 0;
        img.Save(path, new GifEncoder());
        return path;
    }

    [Theory]
    [InlineData("img-resize", "mode=percentage,percent=50", 16, 8)]
    [InlineData("img-crop", "width=16,height=8", 16, 8)]
    [InlineData("img-rotate", "direction=right", 16, 32)]
    [InlineData("img-compress", "level=low", 32, 16)]
    [InlineData("img-watermark", "text=Hi,opacity=100", 32, 16)]
    public void An_animated_gif_keeps_every_frame_and_its_timing(string id, string optText, int w, int h)
    {
        var gif = AnimatedGif(ToolTestSupport.NewTempDir());
        var opts = optText.Split(',').Select(p => (p.Split('=')[0], p.Split('=')[1])).ToArray();
        var r = Run(id, new[] { gif }, opts);
        if (!r.Ok && id == "img-compress") return;
        Assert.True(r.Ok, id + ": " + r.Error);
        using var img = SImage.Load<Rgba32>(r.Outputs[0]);
        Assert.Equal((w, h), (img.Width, img.Height));
        Assert.Equal(3, img.Frames.Count);
        Assert.Equal(new[] { 7, 10, 13 }, Enumerable.Range(0, img.Frames.Count).Select(i => (int)img.Frames[i].Metadata.GetGifMetadata().FrameDelay).ToArray());
        Assert.Equal(0, (int)img.Metadata.GetGifMetadata().RepeatCount);
    }

    // ---------------- odd shapes ----------------

    [Fact]
    public void A_one_pixel_image_and_a_very_wide_one_go_through_every_tool_that_can_take_them()
    {
        var dir = ToolTestSupport.NewTempDir();
        var tiny = ImageToolTestSupport.SolidImage(dir, "tiny.png", 1, 1, new Rgba32(10, 200, 30, 255));
        var wide = ImageToolTestSupport.NoisyImage(dir, "wide.png", "png", 3000, 2);
        var bad = new List<string>();
        foreach (var (file, w, h) in new[] { (tiny, 1, 1), (wide, 3000, 2) })
        {
            var name = IoPath.GetFileName(file);
            var cases = new (string Id, (string, string)[] Opts, Func<int, int, (int, int)> Size)[]
            {
                ("img-resize", new[] { ("mode", "percentage"), ("percent", "100") }, (a, b) => (a, b)),
                ("img-resize", new[] { ("width", "1") }, (a, b) => (1, Math.Max(1, (int)Math.Round(b / (double)a)))),
                ("img-crop", new[] { ("width", "1"), ("height", "1") }, (a, b) => (1, 1)),
                ("img-rotate", new[] { ("direction", "right") }, (a, b) => (b, a)),
                ("img-dpi", new[] { ("dpi", "72") }, (a, b) => (a, b)),
                ("img-to-jpg", Array.Empty<(string, string)>(), (a, b) => (a, b)),
                ("img-convert-any", new[] { ("to", "bmp") }, (a, b) => (a, b)),
                ("img-watermark", new[] { ("text", "X") }, (a, b) => (a, b)),
            };
            foreach (var (id, opts, size) in cases)
            {
                var r = Run(id, new[] { file }, opts);
                if (!r.Ok) { bad.Add($"{name} {id}: {r.Error}"); continue; }
                var info = SImage.Identify(r.Outputs[0]);
                var (ew, eh) = size(w, h);
                if ((info.Width, info.Height) != (ew, eh)) bad.Add($"{name} {id}: {info.Width}x{info.Height} instead of {ew}x{eh}");
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    // ---------------- colour profile ----------------

    [Theory]
    [InlineData("img-resize", "mode=percentage,percent=100")]
    [InlineData("img-crop", "width=20,height=10")]
    [InlineData("img-rotate", "direction=right")]
    [InlineData("img-dpi", "dpi=150")]
    [InlineData("img-watermark", "text=Hi")]
    [InlineData("img-compress", "level=low")]
    public void An_embedded_colour_profile_is_kept_or_the_picture_is_unchanged_in_colour(string id, string optText)
    {
        var dir = ToolTestSupport.NewTempDir();
        // The PNG writer of this engine version has no colour-profile chunk, so the fixture is a JPG.
        var path = IoPath.Combine(dir, "tagged.jpg");
        using (var img = new Image<Rgba32>(40, 20, new Rgba32(180, 40, 90, 255)))
        {
            img.Metadata.IccProfile = new IccProfile(Convert.FromBase64String(SrgbIcc));
            img.Save(path, new JpegEncoder { Quality = 95 });
        }
        using (var check = SImage.Load<Rgba32>(path))
            Assert.NotNull(check.Metadata.IccProfile); // the fixture really carries a profile
        var opts = optText.Split(',').Select(p => (p.Split('=')[0], p.Split('=')[1])).ToArray();
        var r = Run(id, new[] { path }, opts);
        if (!r.Ok && id == "img-compress") return;
        Assert.True(r.Ok, id + ": " + r.Error);
        using var outImg = SImage.Load<Rgba32>(r.Outputs[0]);
        Assert.NotNull(outImg.Metadata.IccProfile);
        Assert.Equal(588, outImg.Metadata.IccProfile!.ToByteArray().Length);
    }
}
