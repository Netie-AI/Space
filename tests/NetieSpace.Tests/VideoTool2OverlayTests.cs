using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>
/// vid-add-text, vid-add-image, vid-subtitles, vid-remove-logo. Every assertion decodes frames of the file a
/// person receives and compares them with the same frame of the input: pixels must change where the overlay is
/// and stay put far away from it.
/// </summary>
public class VideoTool2OverlayTests
{
    // Flat mid-grey clip, 320x180, 15 fps: frame n is at n/15 s.
    private const int W = 320, H = 180;

    // ------------------------------------------------------------------ vid-add-text

    [Fact]
    public void Add_text_changes_pixels_where_the_text_goes_and_leaves_a_far_corner_alone()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4");
        var before = IoFile.ReadAllBytes(input);

        var output = RunOk1("vid-add-text", Out(dir), new[] { input },
            ("text", "HELLO"), ("position", "bottom-right"), ("size", "40"), ("color", "#FF0000"));

        var src = FrameAt(input, 10);
        var dst = FrameAt(output, 10);
        Assert.True(dst.Diff(src, 160, 110, 160, 70) > 4, "text region should differ");
        Assert.True(dst.Diff(src, 0, 0, 120, 60) < 1.5, "far top-left corner should be untouched");
        // the colour asked for really is what was drawn
        Assert.True(dst.Count(160, 110, 160, 70, (r, g, b) => r > 200 && g < 70 && b < 70) > 60, "red glyph pixels expected");
        // picture size and sound survive; input is untouched
        var info = Probe(output);
        Assert.Equal((W, H), (info.Width, info.Height));
        Assert.NotNull(info.AudioCodec);
        Assert.Equal(before, IoFile.ReadAllBytes(input));
        Assert.StartsWith(IoPath.GetFullPath(Out(dir)), IoPath.GetFullPath(output));
    }

    [Theory]
    [InlineData("top-left", 0, 0, 160, 70, 160, 110)]
    [InlineData("middle-center", 80, 60, 160, 60, 0, 0)]
    [InlineData("bottom-center", 80, 110, 160, 70, 0, 0)]
    public void Add_text_position_grid_moves_the_text(string position, int x, int y, int w, int h, int farX, int farY)
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", audio: false);
        var output = RunOk1("vid-add-text", Out(dir), new[] { input }, ("text", "MOVE"), ("position", position), ("size", "36"));
        var src = FrameAt(input, 6);
        var dst = FrameAt(output, 6);
        Assert.True(dst.Diff(src, x, y, w, h) > 3, position + ": text region should differ");
        // the opposite corner (or the top-left for centred text) is clean
        Assert.True(dst.Diff(src, farX, farY, 60, 50) < 1.5, position + ": far region should be clean");
    }

    [Fact]
    public void Add_text_shows_only_inside_the_start_end_window()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 3, audio: false);
        var output = RunOk1("vid-add-text", Out(dir), new[] { input },
            ("text", "WINDOW"), ("position", "middle-center"), ("size", "40"), ("start", "1"), ("end", "2"));

        double Region(int n) => FrameAt(output, n).Diff(FrameAt(input, n), 60, 60, 200, 60);
        Assert.True(Region(5) < 1.5, "before start (0.33 s)");
        Assert.True(Region(22) > 3, "inside window (1.47 s)");
        Assert.True(Region(40) < 1.5, "after end (2.67 s)");
    }

    [Fact]
    public void Add_text_background_box_paints_behind_the_text()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", audio: false);
        var output = RunOk1("vid-add-text", Out(dir), new[] { input },
            ("text", "BOX"), ("position", "middle-center"), ("size", "40"), ("color", "#FFFFFF"), ("background", "#000000"));
        var dst = FrameAt(output, 3);
        Assert.True(dst.Count(60, 50, 200, 80, (r, g, b) => r < 30 && g < 30 && b < 30) > 400, "black box pixels expected");
    }

    [Fact]
    public void Add_text_cannot_be_turned_into_filter_syntax_or_expanded()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", audio: false);
        // quotes, colons, commas, brackets, a drawbox injection that would paint the whole frame red, and %{} expansion
        const string hostile = "it's a:b,c;d[e]f\\g%{pts}',drawbox=x=0:y=0:w=iw:h=ih:color=red@1:t=fill,drawtext=text='x";
        var output = RunOk1("vid-add-text", Out(dir), new[] { input },
            ("text", hostile), ("position", "bottom-center"), ("size", "10"));
        var src = FrameAt(output, 0);
        var ref0 = FrameAt(input, 0);
        Assert.True(src.Diff(ref0, 0, 0, W, 100) < 1.5, "nothing may be painted outside the text line");
        Assert.True(src.Diff(ref0, 0, 150, W, 30) > 1, "the text itself is drawn");
        Assert.Equal(0, src.Count(0, 0, W, 100, (r, g, b) => r > 200 && g < 60 && b < 60));
    }

    [Fact]
    public void Add_text_names_the_option_that_is_wrong()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        string Err(params (string, string)[] o) { var r = Run("vid-add-text", Out(dir), new[] { input }, o); Assert.False(r.Ok); return r.Error!; }
        Assert.Contains("text", Err());
        Assert.Contains("text", Err(("text", "   ")));
        Assert.Contains("'size'", Err(("text", "a"), ("size", "4")));
        Assert.Contains("'color'", Err(("text", "a"), ("color", "red")));
        Assert.Contains("'position'", Err(("text", "a"), ("position", "up")));
        Assert.Contains("'end'", Err(("text", "a"), ("start", "1"), ("end", "1")));
        Assert.Contains("'start'", Err(("text", "a"), ("start", "9")));
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void Same_named_inputs_from_two_folders_get_different_outputs()
    {
        var dir = NewDir();
        var a = MakeFlat(IoPath.Combine(dir, "a"), "clip.mp4", seconds: 1, audio: false);
        var b = MakeFlat(IoPath.Combine(dir, "b"), "clip.mp4", seconds: 1, color: "blue", audio: false);
        var o1 = RunOk1("vid-add-text", Out(dir), new[] { a }, ("text", "x"));
        var o2 = RunOk1("vid-add-text", Out(dir), new[] { b }, ("text", "x"));
        Assert.NotEqual(o1, o2);
        Assert.NotEqual(IoFile.ReadAllBytes(o1), IoFile.ReadAllBytes(o2));
    }

    [Fact]
    public async Task Add_text_reports_progress_and_stops_on_cancel_without_leftovers()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "slow.mp4", seconds: 6, size: "1280x720", rate: 30, audio: false);
        var progress = new ListProgress();
        var tool = Registry.Find("vid-add-text")!;
        var req = new ToolRequest(new[] { input }, Out(dir), new Dictionary<string, string> { ["text"] = "progress" });
        var done = await ToolRunner.RunAsync(tool, req, progress, CancellationToken.None);
        Assert.True(done.Ok, done.Error);
        Assert.NotEmpty(progress.Reports);
        Assert.Equal(100, progress.Reports[^1].Percent);

        var outDir2 = IoPath.Combine(dir, "out2");
        using var cts = new CancellationTokenSource();
        var task = ToolRunner.RunAsync(tool, req with { OutDir = outDir2 }, null, cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        var r = await task;
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
        Assert.Empty(Files(outDir2));
    }

    // ------------------------------------------------------------------ vid-add-image

    private static string Png(string dir, string name, string color, string size = "100x100") =>
        MakeImage(dir, name, $"color=c={color}:s={size}");

    [Fact]
    public void Add_image_lands_at_the_grid_position_at_the_asked_size()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4");
        var logo = Png(dir, "logo.png", "red");

        var output = RunOk1("vid-add-image", Out(dir), new[] { input, logo },
            ("position", "top-right"), ("scale", "25"));

        var src = FrameAt(input, 8);
        var dst = FrameAt(output, 8);
        // 25 % of 320 = 80 px wide, 3 % margin from the top-right corner
        Assert.True(dst.Diff(src, 240, 10, 70, 70) > 60, "overlay region should differ a lot");
        Assert.True(dst.Diff(src, 0, 100, 120, 80) < 1.5, "bottom-left must be untouched");
        var y = 30;
        var redRun = Enumerable.Range(0, W).Count(x => { var p = dst.At(x, y); return p.R > 200 && p.G < 60 && p.B < 60; });
        Assert.InRange(redRun, 74, 86);
        Assert.True(dst.At(300, 30).R > 200 && dst.At(20, 30).R < 150, "red at the right, grey at the left");
        Assert.NotNull(Probe(output).AudioCodec);
        Assert.InRange(Probe(output).Duration, 2.6, 3.4);
    }

    [Fact]
    public void Add_image_opacity_blends_with_the_picture_underneath()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", color: "0x808080", audio: false);
        var logo = Png(dir, "logo.png", "red");
        var output = RunOk1("vid-add-image", Out(dir), new[] { input, logo }, ("position", "middle-center"), ("scale", "50"), ("opacity", "50"));
        var (r, g, b) = FrameAt(output, 5).Mean(140, 70, 40, 40);
        // half red over grey 128: R near 191, G and B near 64
        Assert.InRange(r, 178, 204);
        Assert.InRange(g, 52, 78);
        Assert.InRange(b, 52, 78);
    }

    [Fact]
    public void Add_image_keeps_png_transparency()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", color: "0x808080", audio: false);
        // left half of the logo is fully transparent, right half solid red
        var logo = MakeImage(dir, "half.png", "color=c=red:s=100x100,format=rgba,drawbox=x=0:y=0:w=50:h=100:color=black@0:t=fill:replace=1");
        var output = RunOk1("vid-add-image", Out(dir), new[] { input, logo }, ("position", "middle-center"), ("scale", "50"));
        var dst = FrameAt(output, 4);
        // overlay is 160 wide centred: x 80..240; transparent half is 80..160
        var (lr, lg, _) = dst.Mean(95, 70, 50, 40);
        var (rr, rg, _) = dst.Mean(180, 70, 50, 40);
        Assert.InRange(lr, 120, 136);
        Assert.InRange(lg, 120, 136);
        Assert.True(rr > 200 && rg < 60, "solid half is red");
    }

    [Fact]
    public void Add_image_accepts_jpg_and_animated_gif_and_honours_the_time_window()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", audio: false);
        var jpg = MakeImage(dir, "logo.jpg", "color=c=blue:s=120x120");
        var o1 = RunOk1("vid-add-image", Out(dir), new[] { input, jpg }, ("position", "bottom-left"), ("scale", "30"), ("start", "1"), ("end", "2"));
        double Region(string f, int n) => FrameAt(f, n).Diff(FrameAt(input, n), 5, 100, 90, 75);
        Assert.True(Region(o1, 5) < 1.5);
        Assert.True(Region(o1, 22) > 40);
        Assert.True(Region(o1, 40) < 1.5);

        var gif = IoPath.Combine(dir, "anim.gif");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "color=c=lime:s=60x60:r=5", "-t", "1", gif);
        var o2 = RunOk1("vid-add-image", Out(dir), new[] { input, gif }, ("position", "top-left"), ("scale", "20"));
        var (r, g, b) = FrameAt(o2, 35).Mean(15, 15, 30, 25); // after the 1 s gif has looped
        Assert.True(g > 200 && r < 90 && b < 90, "looping gif still covers the corner late in the clip");
    }

    [Fact]
    public void Add_image_needs_one_video_and_one_picture()
    {
        var dir = NewDir();
        var a = MakeFlat(dir, "a.mp4", seconds: 1, audio: false);
        var b = MakeFlat(dir, "b.mp4", seconds: 1, audio: false);
        var r = Run("vid-add-image", Out(dir), new[] { a, b });
        Assert.False(r.Ok);
        Assert.Contains("one video and one image", r.Error);
        var one = Run("vid-add-image", Out(dir), new[] { a });
        Assert.False(one.Ok);
        Assert.Contains("at least 2", one.Error);
        var bad = Run("vid-add-image", Out(dir), new[] { a, Png(dir, "l.png", "red") }, ("scale", "200"));
        Assert.False(bad.Ok);
        Assert.Contains("'scale'", bad.Error);
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-subtitles

    [Fact]
    public void Subtitles_are_burned_in_only_while_the_cue_is_active()
    {
        var dir = NewDir();
        // a folder name with an apostrophe, a colon and a space: the classic trouble for the filter graph
        var tricky = IoPath.Combine(dir, "Mom's: clips");
        var input = MakeFlat(tricky, "clip.mp4", seconds: 3, audio: false);
        var srt = IoPath.Combine(tricky, "talk.srt");
        WriteSrt(srt, (1.0, 2.0, "Hello there"));

        var output = RunOk1("vid-subtitles", Out(dir), new[] { input, srt }, ("size", "10"));

        double Bottom(int n) => FrameAt(output, n).Diff(FrameAt(input, n), 20, 120, 280, 60);
        double Top(int n) => FrameAt(output, n).Diff(FrameAt(input, n), 0, 0, W, 90);
        Assert.True(Bottom(5) < 1.0, "before the cue (0.33 s)");
        Assert.True(Bottom(22) > 2.5, "during the cue (1.47 s)");
        Assert.True(Bottom(40) < 1.0, "after the cue (2.67 s)");
        Assert.True(Top(22) < 1.0, "the upper half stays clean");
        Assert.True(FrameAt(output, 22).Count(20, 120, 280, 60, (r, g, b) => r > 235 && g > 235 && b > 235) > 30, "default text is white");
        Assert.Empty(Files(IoPath.GetTempPath()).Where(f => IoPath.GetFileName(f).StartsWith("_ns_sub_")));
    }

    [Fact]
    public void Subtitles_take_a_vtt_through_the_option_and_honour_colour_and_position()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        var vtt = IoPath.Combine(dir, "talk.vtt");
        IoFile.WriteAllText(vtt, "WEBVTT\n\n00:00:00.000 --> 00:00:02.000\nTop text\n");
        var output = RunOk1("vid-subtitles", Out(dir), new[] { input },
            ("subtitles", vtt), ("position", "top-left"), ("color", "#FF0000"), ("size", "12"));
        var dst = FrameAt(output, 10);
        var src = FrameAt(input, 10);
        Assert.True(dst.Count(0, 0, 200, 70, (r, g, b) => r > 200 && g < 70 && b < 70) > 20, "red text at the top-left");
        Assert.True(dst.Diff(src, 0, 100, W, 80) < 1.0, "bottom is clean");
    }

    [Fact]
    public void Subtitles_refuse_a_file_without_cues_and_a_missing_file()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        var empty = IoPath.Combine(dir, "empty.srt");
        IoFile.WriteAllText(empty, "just some words\n");
        var r = Run("vid-subtitles", Out(dir), new[] { input, empty });
        Assert.False(r.Ok);
        Assert.Contains("no subtitle cues", r.Error);
        var none = Run("vid-subtitles", Out(dir), new[] { input });
        Assert.False(none.Ok);
        Assert.Contains("subtitles", none.Error);
        var missing = Run("vid-subtitles", Out(dir), new[] { input }, ("subtitles", IoPath.Combine(dir, "nope.vtt")));
        Assert.False(missing.Ok);
        Assert.Contains("subtitles", missing.Error);
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-remove-logo

    [Fact]
    public void Remove_logo_fills_the_box_from_its_surroundings_and_leaves_the_rest()
    {
        var dir = NewDir();
        // grey picture with a bright white "logo" at x 30..90, y 20..50
        var input = MakeLavfi(dir, "logo.mp4", "color=c=0x606060:s=320x180:r=15,drawbox=x=30:y=20:w=60:h=30:color=white:t=fill");
        Assert.True(FrameAt(input, 3).MeanLuma(35, 25, 50, 20) > 240);

        var output = RunOk1("vid-remove-logo", Out(dir), new[] { input },
            ("x", "26"), ("y", "16"), ("width", "68"), ("height", "38"));

        var dst = FrameAt(output, 3);
        Assert.True(dst.MeanLuma(35, 25, 50, 20) < 130, "the white box is gone");
        Assert.True(dst.Diff(FrameAt(input, 3), 150, 80, 150, 90) < 1.5, "far region untouched");
    }

    [Fact]
    public void Remove_logo_checks_the_rectangle_against_the_picture()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        string Err(params (string, string)[] o) { var r = Run("vid-remove-logo", Out(dir), new[] { input }, o); Assert.False(r.Ok); return r.Error!; }
        Assert.Contains("'x'", Err(("y", "5"), ("width", "10"), ("height", "10")));
        Assert.Contains("'x'", Err(("x", "0"), ("y", "5"), ("width", "10"), ("height", "10")));
        Assert.Contains("'width'", Err(("x", "300"), ("y", "5"), ("width", "30"), ("height", "10")));
        Assert.Contains("'height'", Err(("x", "5"), ("y", "170"), ("width", "10"), ("height", "20")));
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void Remove_logo_only_inside_the_time_window()
    {
        var dir = NewDir();
        var input = MakeLavfi(dir, "logo.mp4", "color=c=0x606060:s=320x180:r=15,drawbox=x=30:y=20:w=60:h=30:color=white:t=fill", seconds: 3);
        var output = RunOk1("vid-remove-logo", Out(dir), new[] { input },
            ("x", "26"), ("y", "16"), ("width", "68"), ("height", "38"), ("start", "1"), ("end", "2"));
        Assert.True(FrameAt(output, 5).MeanLuma(35, 25, 50, 20) > 235, "logo still there before the window");
        Assert.True(FrameAt(output, 22).MeanLuma(35, 25, 50, 20) < 130, "logo gone inside the window");
        Assert.True(FrameAt(output, 40).MeanLuma(35, 25, 50, 20) > 235, "logo back after the window");
    }
}
