using System.Globalization;
using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>vid-adjust, vid-filters, vid-audio-cut-merge, vid-slideshow, vid-gif-editor: read back from decoded frames and audio.</summary>
public class VideoTool2MediaTests
{
    // ------------------------------------------------------------------ helpers

    private static double ChromaSpread(Frame f)
    {
        double sum = 0;
        for (var y = 0; y < f.H; y++)
            for (var x = 0; x < f.W; x++)
            {
                var (r, g, b) = f.At(x, y);
                sum += Math.Abs(r - g) + Math.Abs(g - b);
            }
        return sum / (f.W * f.H);
    }

    private static double LumaStdDev(Frame f)
    {
        double sum = 0, sq = 0;
        var n = f.W * f.H;
        for (var y = 0; y < f.H; y++)
            for (var x = 0; x < f.W; x++) { var l = f.Luma(x, y); sum += l; sq += l * l; }
        var mean = sum / n;
        return Math.Sqrt(Math.Max(0, sq / n - mean * mean));
    }

    private static string TestSrc(string dir, string name = "src.mp4", double seconds = 2) =>
        MakeVideo(dir, name, seconds, audio: false);

    /// <summary>Colourful picture squeezed into a middle tone range, so contrast has room to move both ways.</summary>
    private static string MidTone(string dir) =>
        MakeLavfi(dir, "mid.mp4", "testsrc=size=320x180:rate=15,eq=contrast=0.5", seconds: 2);

    /// <summary>Mean volume of a time slice of an audio file.</summary>
    private static double SliceDb(string path, double from, double length)
    {
        var (_, _, err) = Exec("ffmpeg", "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture), "-t",
            length.ToString(CultureInfo.InvariantCulture), "-i", path, "-vn", "-af", "volumedetect", "-f", "null", "-");
        var m = System.Text.RegularExpressions.Regex.Match(err, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        Assert.True(m.Success, "no mean_volume: " + err);
        return m.Groups[1].Value == "-inf" ? -200 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ vid-adjust

    [Fact]
    public void Adjust_brightness_moves_the_picture_up_and_down()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", color: "0x808080", seconds: 1, audio: false);
        var baseLuma = FrameAt(input, 3).MeanLuma(0, 0, 320, 180);
        var up = RunOk1("vid-adjust", Out(dir), new[] { input }, ("brightness", "40"));
        var down = RunOk1("vid-adjust", Out(dir), new[] { input }, ("brightness", "-40"));
        Assert.True(FrameAt(up, 3).MeanLuma(0, 0, 320, 180) > baseLuma + 40);
        Assert.True(FrameAt(down, 3).MeanLuma(0, 0, 320, 180) < baseLuma - 40);
    }

    [Fact]
    public void Adjust_contrast_widens_and_narrows_the_tone_range()
    {
        var dir = NewDir();
        var input = MidTone(dir);
        var baseSd = LumaStdDev(FrameAt(input, 4));
        var high = LumaStdDev(FrameAt(RunOk1("vid-adjust", Out(dir), new[] { input }, ("contrast", "60")), 4));
        var low = LumaStdDev(FrameAt(RunOk1("vid-adjust", Out(dir), new[] { input }, ("contrast", "-60")), 4));
        Assert.True(high > baseSd * 1.15, $"{high} vs {baseSd}");
        Assert.True(low < baseSd * 0.7, $"{low} vs {baseSd}");
    }

    [Fact]
    public void Adjust_saturation_minus_100_leaves_no_colour_and_hue_rotates_it()
    {
        var dir = NewDir();
        var input = TestSrc(dir);
        Assert.True(ChromaSpread(FrameAt(input, 4)) > 20, "the test picture is colourful");
        var grey = RunOk1("vid-adjust", Out(dir), new[] { input }, ("saturation", "-100"));
        Assert.True(ChromaSpread(FrameAt(grey, 4)) < 3, "no colour left");

        var red = MakeFlat(dir, "red.mp4", color: "0xE01010", seconds: 1, audio: false);
        var turned = RunOk1("vid-adjust", Out(dir), new[] { red }, ("hue", "120"));
        var (r, g, b) = FrameAt(turned, 3).Mean(0, 0, 320, 180);
        Assert.True(r < 120 && g > r + 60, $"red turned away from red: {r:0} {g:0} {b:0}");
    }

    [Fact]
    public void Adjust_refuses_to_do_nothing_and_names_a_bad_value()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        var none = Run("vid-adjust", Out(dir), new[] { input });
        Assert.False(none.Ok);
        Assert.Contains("brightness", none.Error);
        var bad = Run("vid-adjust", Out(dir), new[] { input }, ("hue", "400"));
        Assert.False(bad.Ok);
        Assert.Contains("'hue'", bad.Error);
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-filters

    [Fact]
    public void Filter_grayscale_has_equal_chroma_everywhere_and_keeps_the_sound()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "src.mp4", 2);
        Assert.True(ChromaSpread(FrameAt(input, 4)) > 20);
        var o = RunOk1("vid-filters", Out(dir), new[] { input }, ("filter", "grayscale"));
        Assert.True(ChromaSpread(FrameAt(o, 4)) < 3);
        Assert.NotNull(Probe(o).AudioCodec);
        // luma is kept: it is the same picture, only without colour
        Assert.True(MeanAbsDiff(FrameGray(o, 4), FrameGray(input, 4)) < 6, "same picture in grey");
    }

    [Fact]
    public void Filter_sepia_tints_grey_towards_brown()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", color: "0x808080", seconds: 1, audio: false);
        var o = RunOk1("vid-filters", Out(dir), new[] { input }, ("filter", "sepia"), ("strength", "100"));
        var (r, g, b) = FrameAt(o, 3).Mean(0, 0, 320, 180);
        Assert.True(r > g + 8 && g > b + 15, $"warm brown expected, got {r:0} {g:0} {b:0}");
    }

    [Fact]
    public void Filter_blur_and_sharpen_move_edge_energy_opposite_ways()
    {
        var dir = NewDir();
        var input = TestSrc(dir);
        var baseS = FrameAt(input, 4).Sharpness();
        var blur = RunOk1("vid-filters", Out(dir), new[] { input }, ("filter", "blur"), ("strength", "60"));
        var sharp = RunOk1("vid-filters", Out(dir), new[] { input }, ("filter", "sharpen"), ("strength", "80"));
        Assert.True(FrameAt(blur, 4).Sharpness() < baseS * 0.75, "blurred picture has softer edges");
        Assert.True(FrameAt(sharp, 4).Sharpness() > baseS * 1.1, "sharpened picture has harder edges");
    }

    [Fact]
    public void Filter_vignette_darkens_the_corners_but_not_the_centre()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", color: "0x808080", seconds: 1, audio: false);
        var o = RunOk1("vid-filters", Out(dir), new[] { input }, ("filter", "vignette"), ("strength", "90"));
        var f = FrameAt(o, 3);
        Assert.InRange(f.MeanLuma(140, 80, 40, 20), 118, 138);
        Assert.True(f.MeanLuma(0, 0, 20, 20) < 85, "corner is darker");
        Assert.True(f.MeanLuma(300, 160, 20, 20) < 85, "opposite corner is darker");
    }

    [Fact]
    public void Filter_rejects_an_unknown_look_by_naming_the_option()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        var r = Run("vid-filters", Out(dir), new[] { input }, ("filter", "lomo"));
        Assert.False(r.Ok);
        Assert.Contains("'filter'", r.Error);
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-audio-cut-merge

    [Fact]
    public void Audio_cut_keeps_exactly_the_window()
    {
        var dir = NewDir();
        var input = MakeAudio(dir, "tone.wav", seconds: 6);
        var o = RunOk1("vid-audio-cut-merge", Out(dir), new[] { input }, ("start", "1"), ("end", "3.5"), ("format", "wav"));
        var info = Probe(o);
        Assert.InRange(info.Duration, 2.5 - 0.05, 2.5 + 0.05);
        Assert.Equal("pcm_s16le", info.AudioCodec);
        Assert.Null(info.VideoCodec);
        Assert.True(SliceDb(o, 0, 2.4) > -30, "the cut still carries the tone");
    }

    [Fact]
    public void Audio_join_puts_the_files_in_the_given_order()
    {
        var dir = NewDir();
        var loud = MakeAudio(dir, "a-loud.wav", seconds: 2, freq: 440);
        var quiet = IoPath.Combine(dir, "b-quiet.wav");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=44100", "-af", "volume=0.05", "-t", "3", quiet);

        var ab = RunOk1("vid-audio-cut-merge", Out(dir), new[] { loud, quiet }, ("format", "wav"));
        Assert.InRange(Probe(ab).Duration, 4.95, 5.05);
        Assert.True(SliceDb(ab, 0.2, 1.5) > SliceDb(ab, 2.5, 2.0) + 15, "loud part first");

        var ba = RunOk1("vid-audio-cut-merge", Out(dir), new[] { quiet, loud }, ("format", "wav"));
        Assert.True(SliceDb(ba, 0.5, 2.0) + 15 < SliceDb(ba, 3.2, 1.5), "quiet part first");
        Assert.NotEqual(ab, ba);
    }

    [Fact]
    public void Audio_join_then_cut_and_fade_work_together_in_other_formats()
    {
        var dir = NewDir();
        var a = MakeAudio(dir, "a.wav", seconds: 3);
        var b = MakeAudio(dir, "b.wav", seconds: 3, freq: 440);
        var o = RunOk1("vid-audio-cut-merge", Out(dir), new[] { a, b }, ("start", "2"), ("end", "5"), ("format", "flac"), ("fade", "1"));
        var info = Probe(o);
        Assert.Equal("flac", info.AudioCodec);
        Assert.InRange(info.Duration, 2.95, 3.05);
        Assert.True(SliceDb(o, 0, 0.15) + 8 < SliceDb(o, 1.0, 0.8), "fade-in: the very start is quieter than the middle");
        Assert.True(SliceDb(o, 2.85, 0.15) + 8 < SliceDb(o, 1.0, 0.8), "fade-out: the very end is quieter than the middle");
    }

    [Fact]
    public void Audio_cut_merge_checks_its_inputs_and_options()
    {
        var dir = NewDir();
        var a = MakeAudio(dir, "a.wav", seconds: 2);
        string Err(string[] files, params (string, string)[] o) { var r = Run("vid-audio-cut-merge", Out(dir), files, o); Assert.False(r.Ok); return r.Error!; }
        Assert.Contains("'start' or 'end'", Err(new[] { a }));
        Assert.Contains("'end'", Err(new[] { a }, ("start", "1"), ("end", "1")));
        Assert.Contains("'start'", Err(new[] { a }, ("start", "9")));
        Assert.Contains("'format'", Err(new[] { a }, ("start", "1"), ("format", "mid")));
        Assert.Contains("'fade'", Err(new[] { a }, ("start", "1"), ("fade", "99")));
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-slideshow

    [Fact]
    public void Slideshow_lasts_pictures_times_seconds_and_shows_each_picture_in_order()
    {
        var dir = NewDir();
        var red = MakeImage(dir, "1.png", "color=c=red:s=100x100");
        var green = MakeImage(dir, "2.jpg", "color=c=lime:s=200x120");
        var blue = MakeImage(dir, "3.bmp", "color=c=blue:s=64x64");

        var o = RunOk1("vid-slideshow", Out(dir), new[] { red, green, blue },
            ("seconds", "2"), ("width", "160"), ("height", "90"));

        var info = Probe(o);
        Assert.InRange(info.Duration, 6.0 - 0.1, 6.0 + 0.1);
        Assert.Equal((160, 90), (info.Width, info.Height));
        Assert.Equal(150, CountFrames(o));
        Assert.Equal("h264", info.VideoCodec);
        var f1 = FrameAt(o, 25).Mean(60, 30, 40, 30);   // 1.0 s
        var f2 = FrameAt(o, 75).Mean(60, 30, 40, 30);   // 3.0 s
        var f3 = FrameAt(o, 125).Mean(60, 30, 40, 30);  // 5.0 s
        Assert.True(f1.R > 200 && f1.G < 60 && f1.B < 60, "first picture is red");
        Assert.True(f2.G > 200 && f2.R < 60 && f2.B < 60, "second picture is green");
        Assert.True(f3.B > 200 && f3.R < 60 && f3.G < 60, "third picture is blue");
        // the 100x100 red square is padded, not stretched: black bars left and right
        Assert.True(FrameAt(o, 5).MeanLuma(0, 0, 20, 90) < 20, "black bar at the left edge");
    }

    [Fact]
    public void Slideshow_crop_fills_the_frame_and_music_does_not_change_the_length()
    {
        var dir = NewDir();
        var red = MakeImage(dir, "1.png", "color=c=red:s=100x100");
        var blue = MakeImage(dir, "2.png", "color=c=blue:s=300x100");
        var music = MakeAudio(dir, "m.wav", seconds: 10);
        var o = RunOk1("vid-slideshow", Out(dir), new[] { red, blue, music },
            ("seconds", "1"), ("width", "160"), ("height", "90"), ("fit", "crop"));
        var info = Probe(o);
        Assert.InRange(info.Duration, 1.9, 2.15);
        Assert.NotNull(info.AudioCodec);
        var left = FrameAt(o, 5).Mean(0, 0, 20, 90);
        Assert.True(left.R > 200 && left.G < 60, "cropped, so no black bar");
        Assert.True(SliceDb(o, 0.2, 1.5) > -35, "the music is audible");
    }

    [Fact]
    public void Slideshow_refuses_what_it_cannot_do()
    {
        var dir = NewDir();
        var png = MakeImage(dir, "1.png", "color=c=red:s=64x64");
        var clip = MakeFlat(dir, "c.mp4", seconds: 1, audio: false);
        string Err(string[] files, params (string, string)[] o) { var r = Run("vid-slideshow", Out(dir), files, o); Assert.False(r.Ok); return r.Error!; }
        Assert.Contains("not built in this version", Err(new[] { png, clip }));
        Assert.Contains("'width'", Err(new[] { png }, ("width", "161")));
        Assert.Contains("'seconds'", Err(new[] { png }, ("seconds", "0")));
        Assert.Contains("at least one picture", Err(new[] { MakeAudio(dir, "m.wav", 1) }));
        Assert.Empty(Files(Out(dir)));
    }

    // ------------------------------------------------------------------ vid-gif-editor

    private static string MakeGif(string dir, string name = "src.gif", double seconds = 3)
    {
        var path = IoPath.Combine(dir, name);
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=160x90:rate=10", "-t",
            seconds.ToString(CultureInfo.InvariantCulture), path);
        return path;
    }

    [Fact]
    public void Gif_editor_trims_resizes_and_speeds_up_a_gif()
    {
        var dir = NewDir();
        var gif = MakeGif(dir);
        Assert.Equal(30, CountFrames(gif));

        var o = RunOk1("vid-gif-editor", Out(dir), new[] { gif },
            ("start", "1"), ("duration", "1"), ("width", "80"), ("speed", "200"));

        var info = Probe(o);
        Assert.Equal("gif", info.VideoCodec);
        Assert.Equal((80, 45), (info.Width, info.Height));
        Assert.InRange(CountFrames(o), 4, 6); // 1 s at 10 fps, twice as fast: 5 frames, half a second
        Assert.InRange(info.Duration, 0.4, 0.62);
        // it starts where the trim says: the first frame resembles the source at 1.0 s, not at 0 s
        var first = FrameGray(o, 0);
        Assert.True(MeanAbsDiff(first, FrameGray(gif, 10)) < MeanAbsDiff(first, FrameGray(gif, 0)));
    }

    [Fact]
    public void Gif_editor_slow_motion_stretches_the_time_and_keeps_the_size_by_default()
    {
        var dir = NewDir();
        var gif = MakeGif(dir, seconds: 2);
        var o = RunOk1("vid-gif-editor", Out(dir), new[] { gif }, ("speed", "50"), ("fps", "10"));
        var info = Probe(o);
        Assert.Equal((160, 90), (info.Width, info.Height));
        Assert.InRange(info.Duration, 3.8, 4.3);
    }

    [Fact]
    public void Gif_editor_also_takes_a_video_and_names_bad_options()
    {
        var dir = NewDir();
        var clip = MakeVideo(dir, "c.mp4", 2, audio: false);
        var o = RunOk1("vid-gif-editor", Out(dir), new[] { clip }, ("duration", "1"), ("width", "100"));
        Assert.Equal("gif", Probe(o).VideoCodec);
        Assert.Equal(100, Probe(o).Width);
        string Err(params (string, string)[] opts) { var r = Run("vid-gif-editor", Out(dir), new[] { clip }, opts); Assert.False(r.Ok); return r.Error!; }
        Assert.Contains("'speed'", Err(("speed", "900")));
        Assert.Contains("'width'", Err(("width", "7")));
        Assert.Contains("'start'", Err(("start", "50")));
        Assert.Contains("'duration'", Err(("duration", "0")));
    }
}
