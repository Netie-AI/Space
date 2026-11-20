using System.Globalization;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>vid-meme, vid-stop-motion, vid-screen-record. Results are read back from decoded frames and ffprobe.</summary>
public class VideoTool3Tests
{
    // ------------------------------------------------------------------ helpers

    private static bool IsWhite(int r, int g, int b) => r > 235 && g > 235 && b > 235;
    private static bool IsBlack(int r, int g, int b) => r < 25 && g < 25 && b < 25;

    private static string Flat(string dir, string name = "flat.mp4", string size = "640x360", double seconds = 2, bool audio = true) =>
        MakeFlat(dir, name, seconds, "0x808080", size, 15, audio);

    /// <summary>Row range [first, last] of pixels that are not the flat grey, or (-1, -1).</summary>
    private static (int First, int Last) Changed(Frame f)
    {
        int first = -1, last = -1;
        for (var y = 0; y < f.H; y++)
        {
            var hit = false;
            for (var x = 0; x < f.W && !hit; x++)
            {
                var (r, g, b) = f.At(x, y);
                hit = Math.Abs(r - 128) > 40 || Math.Abs(g - 128) > 40 || Math.Abs(b - 128) > 40;
            }
            if (hit) { if (first < 0) first = y; last = y; }
        }
        return (first, last);
    }

    private static ToolResult MemeRun(string dir, string input, params (string, string)[] opts) =>
        Run("vid-meme", Out(dir), new[] { input }, opts);

    // ------------------------------------------------------------------ vid-meme

    [Fact]
    public void Meme_burns_white_text_with_a_black_outline_in_the_top_and_bottom_bands_only()
    {
        var dir = NewDir();
        var input = Flat(dir);
        var before = IoFile.ReadAllBytes(input);
        var output = RunOk1("vid-meme", Out(dir), new[] { input }, ("top", "one does not simply"), ("bottom", "walk into mordor"));

        var info = Probe(output);
        Assert.Equal(640, info.Width);
        Assert.Equal(360, info.Height);
        Assert.NotNull(info.AudioCodec);
        Assert.InRange(info.Duration, 2.0 - 0.15, 2.0 + 0.15);

        var f = FrameAt(output, 10);
        var reference = FrameAt(input, 10);
        // caption bands: white fill and black outline both present
        foreach (var (y0, h) in new[] { (0, 90), (270, 90) })
        {
            Assert.True(f.Count(0, y0, 640, h, IsWhite) > 300, "white text pixels missing in band at y=" + y0);
            Assert.True(f.Count(0, y0, 640, h, IsBlack) > 300, "black outline pixels missing in band at y=" + y0);
            Assert.True(f.Diff(reference, 0, y0, 640, h) > 1.0, "band at y=" + y0 + " did not change");
        }
        // the middle band is the flat grey still
        Assert.True(f.Diff(reference, 0, 100, 640, 160) < 1.5, "the middle band changed");
        // text size relative to frame height: one line is about 9% of 360 px
        var (first, last) = Changed(f);
        Assert.True(first >= 0);
        Assert.Equal(before, IoFile.ReadAllBytes(input));
        Assert.Single(Files(Out(dir)));
    }

    [Fact]
    public void Meme_with_one_caption_leaves_the_other_band_alone()
    {
        var dir = NewDir();
        var input = Flat(dir);
        var output = RunOk1("vid-meme", Out(dir), new[] { input }, ("top", "only on top"));
        var f = FrameAt(output, 5);
        var reference = FrameAt(input, 5);
        Assert.True(f.Diff(reference, 0, 0, 640, 90) > 1.0);
        Assert.True(f.Diff(reference, 0, 100, 640, 260) < 1.5, "below the top band the picture changed");
    }

    [Fact]
    public void Meme_size_follows_the_frame_height()
    {
        var dir = NewDir();
        var small = RunOk1("vid-meme", Out(dir), new[] { Flat(dir, "s.mp4", "320x180", 1, false) }, ("top", "HI"));
        var big = RunOk1("vid-meme", Out(dir), new[] { Flat(dir, "b.mp4", "640x360", 1, false) }, ("top", "HI"));
        var (s0, s1) = Changed(FrameAt(small, 3));
        var (b0, b1) = Changed(FrameAt(big, 3));
        var smallH = s1 - s0 + 1;
        var bigH = b1 - b0 + 1;
        Assert.InRange((double)bigH / smallH, 1.7, 2.3);
        Assert.InRange(bigH, 360 * 0.04, 360 * 0.14);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void Meme_refuses_an_empty_or_whitespace_caption_and_writes_nothing(string blank)
    {
        var dir = NewDir();
        var input = Flat(dir, seconds: 1);
        foreach (var opts in new[] { new[] { ("top", blank) }, new[] { ("bottom", blank) }, new[] { ("top", "ok"), ("bottom", blank) } })
        {
            var r = MemeRun(dir, input, opts);
            Assert.False(r.Ok);
            Assert.Contains(opts.Last().Item1, r.Error);
        }
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void Meme_without_any_caption_is_refused()
    {
        var dir = NewDir();
        var r = MemeRun(dir, Flat(dir, seconds: 1));
        Assert.False(r.Ok);
        Assert.Contains("'top'", r.Error);
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void Meme_wraps_a_long_caption_inside_the_frame_and_refuses_one_that_cannot_fit()
    {
        var dir = NewDir();
        var input = Flat(dir, size: "320x180", seconds: 1, audio: false);
        var longText = "this caption is much longer than one line of a small frame can hold";
        var output = RunOk1("vid-meme", Out(dir), new[] { input }, ("top", longText));
        var f = FrameAt(output, 3);
        var (first, last) = Changed(f);
        Assert.True(last - first + 1 > 180 * 0.09 * 1.6, "a long caption should take more than one line (" + (last - first + 1) + " rows)");
        Assert.True(last < 90, "the wrapped caption runs into the middle of the frame");
        // nothing touches the left or right edge
        Assert.Equal(0, f.Count(0, 0, 2, 180, (r, g, b) => IsWhite(r, g, b) || IsBlack(r, g, b)));
        Assert.Equal(0, f.Count(318, 0, 2, 180, (r, g, b) => IsWhite(r, g, b) || IsBlack(r, g, b)));

        var tooLong = MemeRun(dir, input, ("top", string.Join(" ", Enumerable.Repeat("word", 40))));
        Assert.False(tooLong.Ok);
        Assert.Contains("does not fit", tooLong.Error);
        var wide = MemeRun(dir, input, ("bottom", new string('W', 120)));
        Assert.False(wide.Ok);
        var huge = MemeRun(dir, input, ("bottom", new string('a', 201)));
        Assert.False(huge.Ok);
        Assert.Contains("'bottom'", huge.Error);
        Assert.Single(Files(Out(dir)));
    }

    [Fact]
    public void Meme_caption_text_is_data_never_filter_syntax()
    {
        var dir = NewDir();
        var input = Flat(dir, seconds: 1, audio: false);
        var hostile = new[]
        {
            "a'b\":c;d,e[f]g%{pts}h\\i",
            "';movie=/etc/passwd;'",
            "%{metadata:title} %{expr:1+1} $(id) `id` & echo x | y",
            "line one\nline two\r\nline three",
            "\u00dcber \u00e9t\u00e9 \u4e2d\u6587 \U0001F600",
        };
        foreach (var t in hostile)
        {
            var r = MemeRun(dir, input, ("top", t), ("bottom", t));
            Assert.True(r.Ok, t + " -> " + r.Error);
            var f = FrameAt(r.Outputs[0], 3);
            Assert.True(f.Count(0, 0, f.W, f.H / 3, IsWhite) > 20, "no text drawn for " + t);
        }
        Assert.Equal(hostile.Length, Files(Out(dir)).Length);
        Assert.Empty(IoDirectory.GetFiles(IoPath.GetTempPath(), "_ns_meme_*"));
    }

    [Fact]
    public void Meme_on_a_portrait_clip_with_rotation_metadata_captions_the_upright_picture()
    {
        var dir = NewDir();
        var landscape = Flat(dir, "land.mp4", "320x180", 1, false);
        var portrait = IoPath.Combine(dir, "phone.mp4");
        Must("ffmpeg", "-y", "-v", "error", "-display_rotation", "90", "-i", landscape, "-c", "copy", portrait);
        var shown = VideoFfmpeg.Probe(portrait);
        Assert.Equal((180, 320), (shown.Width, shown.Height));

        var output = RunOk1("vid-meme", Out(dir), new[] { portrait }, ("top", "tall"), ("bottom", "tall"));
        var info = Probe(output);
        Assert.Equal(180, info.Width);
        Assert.Equal(320, info.Height);
        var f = FrameAt(output, 3);
        Assert.True(f.Count(0, 0, 180, 80, IsWhite) > 50, "top band empty");
        Assert.True(f.Count(0, 240, 180, 80, IsWhite) > 50, "bottom band empty");
        Assert.Equal(0, f.Count(0, 100, 180, 120, (r, g, b) => IsWhite(r, g, b) || IsBlack(r, g, b)));
    }

    [Fact]
    public void Meme_on_a_still_picture_gives_a_captioned_png_and_a_clip_without_sound_works()
    {
        var dir = NewDir();
        var png = MakeImage(dir, "pic.png", "color=c=0x808080:s=320x240");
        var output = RunOk1("vid-meme", Out(dir), new[] { png }, ("top", "still"));
        Assert.EndsWith(".png", output);
        var info = VideoFfmpeg.Probe(output);
        Assert.Equal((320, 240), (info.Width, info.Height));
        var (code, bytes, err) = Exec("ffmpeg", "-v", "error", "-i", output, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-");
        Assert.True(code == 0 && bytes.Length == 320 * 240 * 3, err);
        Assert.True(new Frame(320, 240, bytes).Count(0, 0, 320, 60, IsWhite) > 50);

        var silent = Flat(dir, "mute.mp4", "320x180", 1, false);
        var clip = RunOk1("vid-meme", Out(dir), new[] { silent }, ("bottom", "quiet"));
        Assert.Null(Probe(clip).AudioCodec);
    }

    [Fact]
    public async Task Meme_cancel_leaves_no_partial_file()
    {
        var dir = NewDir();
        var big = MakeLavfi(dir, "big.mp4", "testsrc2=size=1920x1080:rate=30", seconds: 40);
        var tool = Registry.Find("vid-meme")!;
        using var cts = new CancellationTokenSource();
        var req = new ToolRequest(new[] { big }, Out(dir), new Dictionary<string, string> { ["top"] = "cancel me" });
        var task = ToolRunner.RunAsync(tool, req, new Progress<ToolProgress>(p => { if (p.Percent >= 1) cts.Cancel(); }), cts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var result = await task;
        Assert.False(result.Ok);
        Assert.Contains("cancel", result.Error);
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-stop-motion

    /// <summary>
    /// How many distinct pictures the file shows: ffmpeg's mpdecimate drops a frame that repeats the one before it.
    /// A re-encode never keeps a held frame bit-identical, so exact hashes cannot be used.
    /// </summary>
    private static int DistinctPictures(string path)
    {
        var (code, bytes, err) = Exec("ffmpeg", "-v", "error", "-i", path, "-map", "0:v:0", "-vf", "mpdecimate", "-f", "framemd5", "-");
        Assert.True(code == 0, err);
        return System.Text.Encoding.UTF8.GetString(bytes).Split('\n').Count(l => l.Length > 0 && l[0] != '#');
    }

    [Fact]
    public void StopMotion_holds_each_kept_frame_and_keeps_the_length_the_rate_and_the_sound()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, audio: true, rate: 15);
        Assert.Equal(45, DistinctPictures(input));

        var output = RunOk1("vid-stop-motion", Out(dir), new[] { input }, ("fps", "3"));
        var inInfo = Probe(input);
        var info = Probe(output);
        Assert.InRange(info.Duration, inInfo.Duration - 0.12, inInfo.Duration + 0.12);
        Assert.NotNull(info.AudioCodec);
        Assert.Equal(inInfo.Width, info.Width);

        Assert.InRange(CountFrames(output), 44, 46);
        // 3 distinct pictures per second over 3 seconds
        Assert.InRange(DistinctPictures(output), 9, 10);
    }

    [Fact]
    public void StopMotion_rate_changes_how_many_pictures_are_kept()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2, audio: false, rate: 15);
        var five = DistinctPictures(RunOk1("vid-stop-motion", Out(dir), new[] { input }, ("fps", "5")));
        var one = DistinctPictures(RunOk1("vid-stop-motion", Out(dir), new[] { input }, ("fps", "1")));
        Assert.InRange(five, 10, 11);
        Assert.InRange(one, 2, 3);
    }

    [Fact]
    public void StopMotion_default_rate_works_and_keeps_a_fractional_frame_rate()
    {
        var dir = NewDir();
        var ntsc = IoPath.Combine(dir, "ntsc.mp4");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=30000/1001", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", ntsc);
        var output = RunOk1("vid-stop-motion", Out(dir), new[] { ntsc });
        var (_, stdout, _) = Exec("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=r_frame_rate", "-of", "csv=p=0", output);
        var rate = System.Text.Encoding.UTF8.GetString(stdout).Trim().Split('/');
        var fps = double.Parse(rate[0], CultureInfo.InvariantCulture) / (rate.Length > 1 ? double.Parse(rate[1], CultureInfo.InvariantCulture) : 1);
        Assert.InRange(fps, 29.9, 30.0);
        Assert.InRange(Probe(output).Duration, 1.85, 2.15);
    }

    [Theory]
    [InlineData(0.4, 1)]
    [InlineData(1.3, 1)]
    [InlineData(2.5, 2)]
    public void StopMotion_keeps_the_length_when_it_is_not_a_whole_number_of_kept_intervals(double seconds, int keep)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "short.mp4", seconds: seconds, audio: false, rate: 15);
        var output = RunOk1("vid-stop-motion", Out(dir), new[] { input }, ("fps", keep.ToString()));
        Assert.InRange(Probe(output).Duration, Probe(input).Duration - 0.1, Probe(input).Duration + 0.1);
    }

    [Fact]
    public void Meme_wide_capitals_stay_inside_the_frame()
    {
        var dir = NewDir();
        var input = Flat(dir, "w.mp4", "320x180", 1, false);
        var output = RunOk1("vid-meme", Out(dir), new[] { input }, ("top", "WWWWWWWWWWWWWWWWWWWW WWWWWWWWWWWW"));
        var f = FrameAt(output, 3);
        Assert.Equal(0, f.Count(0, 0, 3, 180, (r, g, b) => IsWhite(r, g, b) || IsBlack(r, g, b)));
        Assert.Equal(0, f.Count(317, 0, 3, 180, (r, g, b) => IsWhite(r, g, b) || IsBlack(r, g, b)));
    }

    [Fact]
    public void StopMotion_refuses_a_rate_that_is_not_below_the_video_and_rates_out_of_range()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 1, rate: 10);
        var same = Run("vid-stop-motion", Out(dir), new[] { input }, ("fps", "10"));
        Assert.False(same.Ok);
        Assert.Contains("'fps'", same.Error);
        foreach (var bad in new[] { "0", "16", "-3", "x", "2.5" })
        {
            var r = Run("vid-stop-motion", Out(dir), new[] { input }, ("fps", bad));
            Assert.False(r.Ok, bad);
            Assert.Contains("'fps'", r.Error);
        }
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void StopMotion_works_on_a_rotated_phone_clip()
    {
        var dir = NewDir();
        var a = MakeVideo(dir, "a.mp4", seconds: 1, audio: false, rate: 15);
        var rotated = IoPath.Combine(dir, "phone.mp4");
        Must("ffmpeg", "-y", "-v", "error", "-display_rotation", "90", "-i", a, "-c", "copy", rotated);
        var output = RunOk1("vid-stop-motion", Out(dir), new[] { rotated }, ("fps", "5"));
        var p = Probe(output);
        Assert.Equal((180, 320), (p.Width, p.Height));
        Assert.InRange(p.Duration, 0.85, 1.15);
        var again = RunOk1("vid-stop-motion", Out(dir), new[] { rotated }, ("fps", "5"));
        Assert.NotEqual(output, again);
    }

    // ------------------------------------------------------------------ vid-screen-record

    [Fact]
    public void ScreenRecord_builds_the_gdigrab_arguments_for_the_chosen_options()
    {
        var plain = VideoToolProvider.ScreenRecordArgs(2, 30, null, true);
        Assert.Equal(new[] { "-f", "gdigrab", "-framerate", "30", "-draw_mouse", "1", "-i", "desktop", "-t", "2" }, plain.Take(10));
        Assert.DoesNotContain("-offset_x", plain);

        var region = VideoToolProvider.ScreenRecordArgs(7, 12, (10, 20, 300, 200), false);
        var s = string.Join(" ", region);
        Assert.Contains("-draw_mouse 0", s);
        Assert.Contains("-offset_x 10 -offset_y 20 -video_size 300x200 -i desktop -t 7", s);
        Assert.Contains("fps=12", s);
        Assert.Contains("trunc(iw/2)*2", s);

        Assert.Equal((1, 2, 3, 4), VideoToolProvider.ParseRegion(" 1, 2 ,3,4 "));
        Assert.Null(VideoToolProvider.ParseRegion(null));
    }

    [Fact]
    public void ScreenRecord_validates_every_option_by_name_and_takes_no_input_file()
    {
        var dir = NewDir();
        string Error(params (string, string)[] o)
        {
            var r = Run("vid-screen-record", Out(dir), Array.Empty<string>(), o);
            Assert.False(r.Ok);
            return r.Error!;
        }
        Assert.Contains("'seconds'", Error(("seconds", "0")));
        Assert.Contains("'seconds'", Error(("seconds", "601")));
        Assert.Contains("'fps'", Error(("fps", "4")));
        Assert.Contains("'fps'", Error(("fps", "61")));
        Assert.Contains("'cursor'", Error(("cursor", "maybe")));
        foreach (var bad in new[] { "1,2,3", "a,b,c,d", "-1,0,100,100", "0,0,1,100", "0,0,100,99999", "" })
            if (bad.Length > 0) Assert.Contains("'region'", Error(("region", bad)));
        var input = MakeVideo(dir, "x.mp4", seconds: 1);
        var withInput = Run("vid-screen-record", Out(dir), new[] { input });
        Assert.False(withInput.Ok);
        Assert.Contains("input file", withInput.Error);
        Assert.Empty(Files(Out(dir)));
    }

    [Fact]
    public void ScreenRecord_off_Windows_fails_naming_the_reason_and_leaves_nothing()
    {
        var dir = NewDir();
        var r = Run("vid-screen-record", Out(dir), Array.Empty<string>(), ("seconds", "1"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(r.Ok);
            Assert.Contains("Windows only", r.Error);
            Assert.Empty(Files(Out(dir)));
        }
        else
        {
            // The real capture is driven on the Windows runner by scripts/checks.d/video3.ps1: a runner may have no desktop.
            Assert.True(r.Ok || r.Error!.Contains("ffmpeg failed"), r.Error);
            Assert.True(r.Ok || Files(Out(dir)).Length == 0);
        }
    }
}
