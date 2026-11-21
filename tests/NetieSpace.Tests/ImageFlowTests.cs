using NetieSpace.Apps.Image;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

/// <summary>W6-3 image flow: the draggable crop box, presets, size targets, the preview source and the debounce.</summary>
public class ImageFlowTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();

    private static Dictionary<string, string> O(params (string K, string V)[] kv) =>
        kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    // ---- crop box maths ----------------------------------------------------------------------------

    [Theory]
    [InlineData("1:1", 4000, 3000, 3000, 3000)]
    [InlineData("1:1", 3000, 4000, 3000, 3000)]
    [InlineData("4:5", 4000, 3000, 2400, 3000)]
    [InlineData("16:9", 4000, 3000, 4000, 2250)]
    [InlineData("16:9", 1000, 2000, 1000, 562)]
    public void The_first_box_for_a_shape_is_the_largest_centred_one(string shape, int w, int h, int ew, int eh)
    {
        var box = CropBox.Initial(w, h, shape);
        var (x, y, pw, ph) = CropBox.ToPixels(box, w, h, shape);
        Assert.Equal(ew, pw);
        Assert.Equal(eh, ph);
        Assert.Equal((w - pw) / 2.0, x, 1.0);
        Assert.Equal((h - ph) / 2.0, y, 1.0);
    }

    [Fact]
    public void A_free_box_starts_inside_the_picture_with_room_to_grow()
    {
        var box = CropBox.Initial(800, 600, "free");
        Assert.True(box.X > 0 && box.Y > 0 && box.Right < 1 && box.Bottom < 1);
    }

    [Fact]
    public void Moving_the_box_stops_at_every_edge()
    {
        var box = new CropRect(0.2, 0.2, 0.5, 0.5);
        Assert.Equal(0.5, CropBox.Move(box, 10, 0).X, 6);
        Assert.Equal(0.0, CropBox.Move(box, -10, 0).X, 6);
        Assert.Equal(0.5, CropBox.Move(box, 0, 10).Y, 6);
        Assert.Equal(0.0, CropBox.Move(box, 0, -10).Y, 6);
    }

    [Theory]
    [InlineData(CropHandle.TopLeft)]
    [InlineData(CropHandle.TopRight)]
    [InlineData(CropHandle.BottomLeft)]
    [InlineData(CropHandle.BottomRight)]
    [InlineData(CropHandle.Top)]
    [InlineData(CropHandle.Bottom)]
    [InlineData(CropHandle.Left)]
    [InlineData(CropHandle.Right)]
    public void Dragging_any_handle_with_a_shape_keeps_the_shape_inside_the_picture(CropHandle handle)
    {
        var rnd = new Random(7);
        foreach (var shape in new[] { "1:1", "4:5", "16:9" })
        {
            var r = CropBox.Ratio(shape)!.Value;
            var box = CropBox.Initial(1600, 1200, shape);
            // start a little smaller than the biggest so every handle has room both ways
            box = CropBox.Resize(box, CropHandle.BottomRight, -0.15, -0.15, 1600, 1200, shape);
            for (var i = 0; i < 200; i++)
            {
                box = CropBox.Resize(box, handle, (rnd.NextDouble() - 0.5) * 0.4, (rnd.NextDouble() - 0.5) * 0.4, 1600, 1200, shape);
                Assert.True(box.X >= -1e-9 && box.Y >= -1e-9 && box.Right <= 1 + 1e-9 && box.Bottom <= 1 + 1e-9, $"{shape} {handle} left the picture: {box}");
                Assert.Equal(r, box.W * 1600 / (box.H * 1200), 3);
                Assert.True(Math.Min(box.W * 1600, box.H * 1200) >= 23.9, $"{shape} {handle} shrank below the floor: {box}");
            }
        }
    }

    [Fact]
    public void A_free_corner_drag_moves_only_its_own_edges()
    {
        var box = new CropRect(0.2, 0.2, 0.5, 0.5);
        var r = CropBox.Resize(box, CropHandle.BottomRight, 0.1, -0.1, 1000, 1000, "free");
        Assert.Equal(0.2, r.X, 6);
        Assert.Equal(0.2, r.Y, 6);
        Assert.Equal(0.6, r.W, 6);
        Assert.Equal(0.4, r.H, 6);
        var past = CropBox.Resize(box, CropHandle.Left, 5, 0, 1000, 1000, "free");
        Assert.True(past.W * 1000 >= 23.9);
    }

    [Fact]
    public void Hit_testing_finds_corners_edges_the_inside_and_nothing()
    {
        // box drawn at 100,100 size 200 x 100, handles grabbed within 8 px
        Assert.Equal(CropHandle.TopLeft, CropBox.HitTest(100, 100, 200, 100, 103, 97, 8, false));
        Assert.Equal(CropHandle.BottomRight, CropBox.HitTest(100, 100, 200, 100, 300, 200, 8, false));
        Assert.Equal(CropHandle.Top, CropBox.HitTest(100, 100, 200, 100, 200, 101, 8, false));
        Assert.Equal(CropHandle.Left, CropBox.HitTest(100, 100, 200, 100, 99, 150, 8, false));
        Assert.Equal(CropHandle.Move, CropBox.HitTest(100, 100, 200, 100, 200, 150, 8, false));
        Assert.Equal(CropHandle.None, CropBox.HitTest(100, 100, 200, 100, 50, 50, 8, false));
        // with a shape only the corners are handles, so an edge is just the inside
        Assert.Equal(CropHandle.Move, CropBox.HitTest(100, 100, 200, 100, 200, 101, 8, true));
    }

    [Theory]
    [InlineData(4000, 3000)]
    [InlineData(3000, 4000)]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1080)]
    public void One_box_drawn_on_a_photo_gives_every_photo_the_same_shape(int w, int h)
    {
        var drawn = CropBox.Initial(4000, 3000, "1:1");
        drawn = CropBox.Resize(drawn, CropHandle.BottomRight, -0.1, -0.1, 4000, 3000, "1:1");
        var o = O(("aspect", "1:1"), ("box", CropBox.ToOption(drawn, 4000, 3000, "1:1")));
        var (x, y, pw, ph) = ImageOps.CropRectFor(w, h, o);
        Assert.Equal(pw, ph);
        Assert.True(x >= 0 && y >= 0 && x + pw <= w && y + ph <= h);
        ImageGeometry.CheckCrop(w, h, x, y, pw, ph);
    }

    [Fact]
    public void The_box_option_round_trips_and_a_bad_one_is_refused_with_the_option_named()
    {
        var box = new CropRect(0.125, 0.25, 0.5, 0.5);
        var (x, y, w, h) = ImageGeometry.ParseBox(CropBox.ToOption(box, 1000, 1000, "free"));
        Assert.Equal((0.125, 0.25, 0.5, 0.5), (x, y, w, h));
        var (cx, cy, zoom) = ImageGeometry.ParseZoomBox(CropBox.ToOption(CropBox.Initial(4000, 3000, "1:1"), 4000, 3000, "1:1"));
        Assert.Equal((0.5, 0.5, 1.0), (cx, cy, zoom));
        foreach (var bad in new[] { "", "1,2,3", "a,b,c,d", "0.5,0.5,0.9,0.9", "-0.1,0,0.5,0.5", "0,0,0,0.5", "NaN,0,0.5,0.5" })
        {
            var ex = Assert.Throws<ArgumentException>(() => ImageGeometry.ParseBox(bad));
            Assert.Contains("box", ex.Message);
        }
        foreach (var bad in new[] { "0.5,0.5", "2,0.5,0.5", "0.5,0.5,0", "0.5,0.5,1.5", "x,0.5,0.5" })
            Assert.Throws<ArgumentException>(() => ImageGeometry.ParseZoomBox(bad));
    }

    // ---- the runners honour the new options ----------------------------------------------------------

    [Fact]
    public async Task A_cover_resize_makes_exactly_the_square_and_never_stretches()
    {
        var d = Dir();
        var src = CornerImage(d, "wide.png", 1200, 800);
        var r = await Run("img-resize", new[] { src }, IoPath.Combine(d, "o"),
            ("mode", "pixels"), ("width", "1080"), ("height", "1080"), ("fit", "cover"));
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((1080, 1080), (img.Width, img.Height));
        // the corners of the 1200 x 800 source were cropped away, not squeezed in: the output corner is white
        AssertClose(new Rgba32(255, 255, 255, 255), img[0, 0], 8, "top-left after a centre crop");
    }

    [Fact]
    public async Task Cover_without_both_sides_says_which_is_missing()
    {
        var d = Dir();
        var src = CornerImage(d, "a.png", 100, 80);
        var r = await Run("img-resize", new[] { src }, IoPath.Combine(d, "o"), ("width", "50"), ("fit", "cover"));
        Assert.False(r.Ok);
        Assert.Contains("height", r.Error);
    }

    [Fact]
    public async Task A_drawn_box_crops_each_photo_to_the_same_shape_and_the_file_has_those_pixels()
    {
        var d = Dir();
        var landscape = SolidImage(d, "land.png", 400, 300, new Rgba32(10, 200, 10, 255));
        var portrait = SolidImage(d, "port.png", 300, 400, new Rgba32(10, 10, 200, 255));
        var box = CropBox.ToOption(CropBox.Initial(400, 300, "1:1"), 400, 300, "1:1");
        var r = await Run("img-crop", new[] { landscape, portrait }, IoPath.Combine(d, "o"), ("aspect", "1:1"), ("box", box));
        Assert.True(r.Ok, r.Error);
        foreach (var o in r.Outputs)
        {
            using var img = Open(o);
            Assert.Equal(img.Width, img.Height);
            Assert.Equal(300, img.Width);
        }
    }

    [Fact]
    public async Task Crop_still_wants_its_numbers_when_there_is_no_box()
    {
        var d = Dir();
        var src = CornerImage(d, "a.png", 100, 80);
        var r = await Run("img-crop", new[] { src }, IoPath.Combine(d, "o"), ("x", "0"));
        Assert.False(r.Ok);
        Assert.Contains("width", r.Error);
    }

    [Fact]
    public async Task Compress_stays_under_the_target_for_a_photograph_without_changing_its_size()
    {
        var d = Dir();
        var src = NoisyImage(d, "photo.jpg", "jpg", 900, 900);
        var before = new FileInfo(src).Length;
        Assert.True(before > 150 * 1024, "fixture must be bigger than the target");
        var r = await Run("img-compress", new[] { src }, IoPath.Combine(d, "o"), ("level", "low"), ("target_kb", "120"));
        Assert.True(r.Ok, r.Error);
        var len = new FileInfo(r.Outputs[0]).Length;
        Assert.True(len <= 120 * 1024, $"{len} bytes");
        Assert.True(len > 40 * 1024, "it should not have been crushed far below the target");
        using var img = Open(r.Outputs[0]);
        Assert.Equal((900, 900), (img.Width, img.Height));
    }

    [Fact]
    public async Task Compress_that_cannot_reach_the_target_fails_loudly_and_writes_nothing()
    {
        var d = Dir();
        var src = NoisyImage(d, "photo.jpg", "jpg", 900, 900);
        var outDir = IoPath.Combine(d, "o");
        var r = await Run("img-compress", new[] { src }, outDir, ("target_kb", "1"));
        Assert.False(r.Ok);
        Assert.Contains("under 1 KB", r.Error);
        Assert.Contains("resize", r.Error);
        Assert.True(!IoDirectory.Exists(outDir) || IoDirectory.GetFiles(outDir).Length == 0);
    }

    [Fact]
    public async Task A_png_target_steps_the_palette_down_until_it_fits()
    {
        var d = Dir();
        var src = NoisyImage(d, "photo.png", "png", 400, 400);
        // calibrate from the 128-colour size, so the 256-colour start cannot fit and the search must step down
        using var img = Open(src);
        var s128 = ImageOps.Encode(img, new EncodeSpec(ImageKind.Png, 0, true, 128)).Length;
        var s256 = ImageOps.Encode(img, new EncodeSpec(ImageKind.Png, 0, true, 256)).Length;
        Assert.True(s256 > s128);
        var target = (int)(s128 / 1024) + 1;
        Assert.True(target * 1024L < s256, "fixture: 256 colours must not already fit");
        var (bytes, spec) = ImageOps.CompressTo(img, ImageKind.Png, "recommended", target * 1024L, "x");
        Assert.True(bytes.Length <= target * 1024L);
        Assert.True(spec.Colors < 256);
        var r = await Run("img-compress", new[] { src }, IoPath.Combine(d, "o"), ("level", "recommended"), ("target_kb", target.ToString()));
        Assert.True(r.Ok, r.Error);
        Assert.True(new FileInfo(r.Outputs[0]).Length <= target * 1024L);
    }

    [Fact]
    public async Task A_file_already_under_the_target_is_copied_not_refused()
    {
        var d = Dir();
        var first = await Run("img-compress", new[] { NoisyImage(d, "n.png", "png", 120, 120) }, IoPath.Combine(d, "once"));
        Assert.True(first.Ok, first.Error);
        var src = first.Outputs[0]; // already compressed: a second pass cannot make it smaller
        var r = await Run("img-compress", new[] { src }, IoPath.Combine(d, "o"), ("target_kb", "300"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Sha(src), Sha(r.Outputs[0]));
        // without a target the same file is still refused as already compact
        var plain = await Run("img-compress", new[] { src }, IoPath.Combine(d, "p"));
        Assert.False(plain.Ok);
        Assert.Contains("already compact", plain.Error);
    }

    [Fact]
    public async Task A_target_of_zero_is_refused_at_the_option()
    {
        var d = Dir();
        var src = NoisyImage(d, "p.jpg", "jpg", 100, 100);
        var r = await Run("img-compress", new[] { src }, IoPath.Combine(d, "o"), ("target_kb", "0"));
        Assert.False(r.Ok);
        Assert.Contains("target_kb", r.Error);
    }

    // ---- presets ----------------------------------------------------------------------------------

    [Fact]
    public void The_three_named_presets_are_in_resize_and_compress_and_every_value_passes_the_runner_validation()
    {
        foreach (var tool in new[] { "img-resize", "img-compress" })
        {
            var labels = ImagePresets.For(tool).Select(p => p.Label).ToList();
            Assert.Contains("Instagram square 1080", labels);
            Assert.Contains("Email small", labels);
        }
        Assert.Contains("Web 1600 under 300 KB", ImagePresets.For("img-compress").Select(p => p.Label));
        Assert.Contains("Web 1600", ImagePresets.For("img-resize").Select(p => p.Label));
        Assert.Empty(ImagePresets.For("img-rotate"));

        var reg = ToolRegistry.Default.All();
        foreach (var p in ImagePresets.Every)
        {
            var tool = reg.Single(t => t.Entry.Id == p.ToolId);
            var opts = p.Options.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
            var file = IoPath.Combine(Dir(), "x.png");
            using (var img = new Image<Rgba32>(4, 4)) SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, file);
            var msg = ToolRunner.Validate(tool.Entry, new ToolRequest(new[] { file }, "out", opts), out _);
            Assert.True(msg is null, $"{p.ToolId}/{p.Label}: {msg}");
        }
    }

    [Fact]
    public void A_preset_marks_itself_picked_only_when_every_value_matches()
    {
        var p = ImagePresets.For("img-compress").Single(x => x.Id == "web-1600-300kb");
        var values = new Dictionary<string, string> { ["level"] = "low", ["target_kb"] = "300" };
        Assert.True(ImagePresets.Matches(p, k => values.GetValueOrDefault(k)));
        values["target_kb"] = "299";
        Assert.False(ImagePresets.Matches(p, k => values.GetValueOrDefault(k)));
    }

    [Fact]
    public async Task The_instagram_preset_through_the_real_tool_gives_1080_square()
    {
        var d = Dir();
        var src = CornerImage(d, "shop.jpg", 1600, 1000, "jpg");
        var p = ImagePresets.For("img-resize").Single(x => x.Id == "instagram-square-1080");
        var r = await Run("img-resize", new[] { src }, IoPath.Combine(d, "o"),
            p.Options.Where(kv => kv.Value.Length > 0).Select(kv => (kv.Key, kv.Value)).ToArray());
        Assert.True(r.Ok, r.Error);
        using var img = Open(r.Outputs[0]);
        Assert.Equal((1080, 1080), (img.Width, img.Height));
    }

    // ---- the preview source ------------------------------------------------------------------------

    [Fact]
    public void The_preview_of_each_tool_reports_the_real_output_size_and_draws_a_reduced_picture()
    {
        var d = Dir();
        var src = CornerImage(d, "big.png", 3000, 2000);
        using var s = FlowSource.Open(src);
        Assert.Equal((3000, 2000), (s.Width, s.Height));
        Assert.True(Math.Max(s.ProxyWidth, s.ProxyHeight) <= FlowSource.DefaultProxyMax);

        var before = s.Before();
        Assert.Equal((3000, 2000), (before.PixelWidth, before.PixelHeight));

        var resized = s.After("img-resize", O(("mode", "pixels"), ("width", "1080"), ("height", "1080"), ("fit", "cover")));
        Assert.Equal((1080, 1080), (resized.PixelWidth, resized.PixelHeight));
        using (var pic = SImage.Load<Rgba32>(resized.Png)) Assert.Equal(pic.Width, pic.Height);

        var rotated = s.After("img-rotate", O(("direction", "right")));
        Assert.Equal((2000, 3000), (rotated.PixelWidth, rotated.PixelHeight));

        var cropped = s.After("img-crop", O(("aspect", "1:1"), ("box", CropBox.ToOption(CropBox.Initial(3000, 2000, "1:1"), 3000, 2000, "1:1"))));
        Assert.Equal((2000, 2000), (cropped.PixelWidth, cropped.PixelHeight));
        using (var pic = SImage.Load<Rgba32>(cropped.Png)) Assert.Equal(pic.Width, pic.Height);
    }

    [Fact]
    public void The_preview_refuses_what_the_runner_would_refuse_with_the_same_words()
    {
        var d = Dir();
        var src = CornerImage(d, "a.png", 200, 100);
        using var s = FlowSource.Open(src);
        var ex = Assert.Throws<ArgumentException>(() => s.After("img-resize", O(("width", "50"), ("fit", "cover"))));
        Assert.Contains("height", ex.Message);
        Assert.Throws<ArgumentException>(() => s.After("img-crop", O(("aspect", "1:1"), ("box", "0.9,0.9,0.5,0.5"))));
        var same = Assert.Throws<InvalidOperationException>(() => s.After("img-convert-any", O(("to", "png"))));
        Assert.Contains("already png", same.Message);
        Assert.Throws<ArgumentException>(() => s.After("img-watermark", O()));
    }

    [Fact]
    public void A_text_watermark_bottom_right_puts_ink_there_in_the_preview_and_in_the_file_and_nowhere_else()
    {
        var d = Dir();
        var src = SolidImage(d, "white.png", 600, 400, new Rgba32(255, 255, 255, 255));
        var o = O(("text", "SHOP"), ("position", "bottom-right"), ("scale", "30"), ("opacity", "100"), ("color", "#000000"));

        using var s = FlowSource.Open(src);
        var frame = s.After("img-watermark", o);
        using var preview = SImage.Load<Rgba32>(frame.Png);
        Assert.True(Dark(preview, preview.Width / 2, preview.Height / 2, preview.Width, preview.Height) > 20, "no ink bottom-right in the preview");
        Assert.Equal(0, Dark(preview, 0, 0, preview.Width / 2, preview.Height / 2));

        var file = ImageOps.WatermarkFile(src, o);
        using var made = SImage.Load<Rgba32>(file.Bytes);
        Assert.Equal((600, 400), (made.Width, made.Height));
        Assert.True(Dark(made, 300, 200, 600, 400) > 100, "no ink bottom-right in the file");
        Assert.Equal(0, Dark(made, 0, 0, 300, 200));
    }

    private static int Dark(Image<Rgba32> img, int x0, int y0, int x1, int y1)
    {
        var n = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                if (img[x, y].R < 128) n++;
        return n;
    }

    [Fact]
    public void The_size_figure_is_the_size_of_the_file_a_run_writes()
    {
        var d = Dir();
        var src = NoisyImage(d, "p.jpg", "jpg", 700, 700);
        var o = O(("level", "recommended"), ("target_kb", "90"));
        var measured = ImageFlow.Measure(src, "img-compress", o);
        Assert.Null(measured.Error);
        Assert.True(measured.Bytes <= 90 * 1024);

        var written = ImageToolRun(src, o, Dir());
        Assert.Equal(measured.Bytes, written);
    }

    private static long ImageToolRun(string src, Dictionary<string, string> o, string outDir)
    {
        var tool = ToolRegistry.Default.All().Single(t => t.Entry.Id == "img-compress");
        var r = ToolRunner.RunAsync(tool, new ToolRequest(new[] { src }, outDir, o), null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(r.Ok, r.Error);
        return new FileInfo(r.Outputs[0]).Length;
    }

    [Fact]
    public void A_refusal_is_a_message_in_the_size_figure_not_an_exception()
    {
        var d = Dir();
        var src = NoisyImage(d, "p.jpg", "jpg", 400, 400);
        var m = ImageFlow.Measure(src, "img-compress", O(("target_kb", "1")));
        Assert.Null(m.Bytes);
        Assert.Contains("under 1 KB", m.Error);
        var bad = ImageFlow.Measure(IoPath.Combine(d, "missing.png"), "img-resize", O(("width", "10")));
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public void Tools_with_a_stage_are_exactly_the_ones_the_page_draws()
    {
        Assert.Equal(FlowStage.Crop, ImageFlow.StageOf("img-crop"));
        Assert.Equal(FlowStage.Watermark, ImageFlow.StageOf("img-watermark"));
        foreach (var id in new[] { "img-resize", "img-rotate", "img-compress", "img-convert-any", "img-to-jpg", "img-from-jpg" })
            Assert.Equal(FlowStage.BeforeAfter, ImageFlow.StageOf(id));
        foreach (var id in new[] { "img-dpi", "img-meme", "img-blur-face", "pdf-merge" })
            Assert.False(ImageFlow.Supports(id));
    }

    // ---- the debounce -------------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_newest_of_five_quick_changes_runs()
    {
        var gates = new List<TaskCompletionSource>();
        Task Wait(TimeSpan _, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            lock (gates) gates.Add(tcs);
            return tcs.Task;
        }

        using var deb = new PreviewDebouncer(wait: Wait);
        var ran = new List<int>();
        Task last = Task.CompletedTask;
        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            last = deb.Schedule(_ => { lock (ran) ran.Add(n); return Task.CompletedTask; });
        }
        TaskCompletionSource[] snapshot;
        lock (gates) snapshot = gates.ToArray();
        Assert.Equal(5, snapshot.Length);
        snapshot[^1].SetResult(); // the delay of the newest elapses; the four before it were cancelled
        await last;
        Assert.Equal(new[] { 5 }, ran);
    }

    [Fact]
    public async Task Newer_work_cancels_older_work_that_is_already_running()
    {
        using var deb = new PreviewDebouncer(wait: (_, _) => Task.CompletedTask);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawCancel = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = deb.Schedule(async ct =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { sawCancel.SetResult(true); throw; }
        });
        await started.Task;
        var ranSecond = false;
        await deb.Schedule(_ => { ranSecond = true; return Task.CompletedTask; });
        Assert.True(await sawCancel.Task);
        await first;
        Assert.True(ranSecond);
    }

    [Fact]
    public async Task Work_that_throws_is_reported_not_swallowed()
    {
        using var deb = new PreviewDebouncer(wait: (_, _) => Task.CompletedTask);
        Exception? seen = null;
        deb.Failed += ex => seen = ex;
        await deb.Schedule(_ => throw new InvalidOperationException("boom"));
        Assert.Equal("boom", seen?.Message);
    }

    [Fact]
    public void The_wait_before_a_preview_leaves_most_of_the_300_ms_for_drawing_it()
    {
        Assert.True(PreviewDebouncer.DefaultDelay <= TimeSpan.FromMilliseconds(150));
    }
}

/// <summary>Timing of the preview itself, alone on the machine so another test cannot slow it.</summary>
[Collection(IsolatedMeasurementCollection.Name)]
public class ImageFlowTimingTests
{
    [Fact]
    public void Drawing_a_preview_from_the_reduced_copy_fits_the_rest_of_the_300_ms()
    {
        var d = ToolTestSupport.NewTempDir();
        var src = ImageToolTestSupport.NoisyImage(d, "big.jpg", "jpg", 3000, 2000);
        using var s = FlowSource.Open(src);
        var o = new Dictionary<string, string> { ["mode"] = "pixels", ["width"] = "1080", ["height"] = "1080", ["fit"] = "cover" };
        s.After("img-resize", o); // warm the JIT: the person's first drag is not what is measured
        var best = double.MaxValue;
        for (var i = 0; i < 7; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            s.After("img-resize", o);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        Assert.True(best + PreviewDebouncer.DefaultDelay.TotalMilliseconds < 300,
            $"best of 7 drawings took {best:0} ms, plus the {PreviewDebouncer.DefaultDelay.TotalMilliseconds:0} ms wait");
    }
}
