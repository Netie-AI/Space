using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Second-pass adversarial checks on the video tools: odd shapes, hostile names, edge options.</summary>
public sealed class VideoToolAttackTests
{
    [Fact]
    public void Resize_pad_with_an_8_digit_colour_uses_the_RGB_part()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2, size: "320x180");
        var o = RunOk1("vid-resize", IoPath.Combine(d, "out"), new[] { src },
            ("width", "320"), ("height", "320"), ("mode", "pad"), ("color", "#ff0000ff"));
        var (code, bytes, err) = Exec("ffmpeg", "-v", "error", "-i", o, "-vf", "select=eq(n\\,0),crop=2:2:0:0,format=rgb24", "-frames:v", "1", "-f", "rawvideo", "-");
        Assert.True(code == 0, err);
        Assert.True(bytes[0] > 200 && bytes[1] < 60 && bytes[2] < 60, "corner pixel was " + bytes[0] + "," + bytes[1] + "," + bytes[2]);
    }

    [Fact]
    public void Crop_with_odd_offset_takes_exactly_that_window()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2, size: "320x180", crf: 4);
        var o = RunOk1("vid-crop", IoPath.Combine(d, "out"), new[] { src },
            ("x", "1"), ("y", "1"), ("width", "100"), ("height", "60"));
        var p = Probe(o);
        Assert.Equal((100, 60), (p.Width, p.Height));
        byte[] Raw(string f, string vf) => Exec("ffmpeg", "-v", "error", "-i", f, "-vf", vf + ",format=gray", "-frames:v", "1", "-f", "rawvideo", "-").Out;
        var got = Raw(o, "null");
        var want = Raw(src, "crop=100:60:1:1:exact=1");
        var wrong = Raw(src, "crop=100:60:0:0");
        Assert.True(MeanAbsDiff(want, got) < MeanAbsDiff(wrong, got), "window is off by one pixel: " + MeanAbsDiff(want, got) + " vs " + MeanAbsDiff(wrong, got));
    }

    [Theory]
    [InlineData("vid-rotate", "angle", "90")]
    [InlineData("vid-flip", "axis", "vertical")]
    [InlineData("vid-speed", "speed", "150")]
    [InlineData("vid-reverse", "", "")]
    [InlineData("vid-loop", "count", "3")]
    [InlineData("vid-compress", "quality", "low")]
    [InlineData("vid-volume", "volume", "50")]
    public void Odd_size_ugly_name_single_input_tools(string id, string key, string val)
    {
        var d = NewDir();
        var src = MakeVideo(IoPath.Combine(d, "in dir"), "Ünï çlip it's 100%.mp4", 2, audio: id == "vid-volume", size: "322x182", crf: 4);
        var opts = key == "" ? Array.Empty<(string, string)>() : new[] { (key, val) };
        var o = RunOk1(id, IoPath.Combine(d, "new", "deeper"), new[] { src }, opts);
        var p = Probe(o);
        Assert.NotNull(p.VideoCodec);
        Assert.True(p.Width % 2 == 0 && p.Height % 2 == 0);
    }

    [Fact]
    public void Speed_200_halves_the_duration_and_frames()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 4);
        var o = RunOk1("vid-speed", d, new[] { src }, ("speed", "200"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 1.8, 2.3);
        Assert.NotNull(p.AudioCodec);
        Assert.InRange(CountFrames(o), 28, 32);
    }

    [Fact]
    public void Speed_25_makes_it_four_times_longer()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2);
        var o = RunOk1("vid-speed", d, new[] { src }, ("speed", "25"));
        Assert.InRange(Probe(o).Duration, 7.5, 8.6);
    }

    [Fact]
    public void Trim_delete_on_webm()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.webm", 4);
        var o = RunOk1("vid-trim", d, new[] { src }, ("start", "1"), ("end", "2"), ("mode", "delete"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 2.7, 3.3);
        Assert.Equal("vp9", p.VideoCodec);
    }

    [Fact]
    public void Trim_end_past_the_clip_is_clamped_and_start_past_it_fails()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 3);
        var o = RunOk1("vid-trim", d, new[] { src }, ("start", "1"), ("end", "99"));
        Assert.InRange(Probe(o).Duration, 1.8, 2.3);
        var r = Run("vid-trim", d, new[] { src }, ("start", "5"));
        Assert.False(r.Ok);
        Assert.Contains("start", r.Error);
    }

    [Fact]
    public void Trim_no_audio_clip_and_clock_times()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 4, audio: false);
        var o = RunOk1("vid-trim", d, new[] { src }, ("start", "0:01"), ("end", "0:00:03.5"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 2.2, 2.8);
        Assert.Null(p.AudioCodec);
    }

    [Fact]
    public void Split_parts_cover_the_whole_clip()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 5);
        var outs = RunOk("vid-split", d, new[] { src }, ("at", "1.5, 3"));
        Assert.Equal(3, outs.Count);
        Assert.InRange(outs.Sum(x => Probe(x).Duration), 4.8, 5.3);
        Assert.InRange(Probe(outs[0]).Duration, 1.3, 1.8);
        Assert.InRange(outs.Sum(CountFrames), 73, 77);
    }

    [Fact]
    public void Split_bad_cut_points_fail_clearly()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 3);
        foreach (var at in new[] { "0", "3", "9", "abc", "-1" })
        {
            var r = Run("vid-split", d, new[] { src }, ("at", at));
            Assert.False(r.Ok, at);
            Assert.Contains("at", r.Error);
        }
    }

    [Fact]
    public void Merge_mixed_sizes_one_without_audio()
    {
        var d = NewDir();
        var a = MakeVideo(IoPath.Combine(d, "x"), "same.mp4", 2, size: "320x180");
        var b = MakeVideo(IoPath.Combine(d, "y"), "same.mp4", 3, audio: false, size: "180x320");
        var o = RunOk1("vid-merge", IoPath.Combine(d, "out"), new[] { a, b });
        var p = Probe(o);
        Assert.InRange(p.Duration, 4.8, 5.3);
        Assert.Equal((320, 180), (p.Width, p.Height));
        Assert.NotNull(p.AudioCodec);
        Assert.InRange(CountFrames(o), 73, 77);
    }

    [Fact]
    public void Merge_odd_first_video_and_webm_second()
    {
        var d = NewDir();
        var a = MakeVideo(d, "a.avi", 2, size: "321x181");
        var b = MakeVideo(d, "b.webm", 2);
        var o = RunOk1("vid-merge", d, new[] { a, b });
        Assert.InRange(Probe(o).Duration, 3.8, 4.4);
    }

    [Fact]
    public void Mute_and_extract_audio_same_name_twice()
    {
        var d = NewDir();
        var a = MakeVideo(IoPath.Combine(d, "1"), "My Clip it's 100%.mp4", 2);
        var b = MakeVideo(IoPath.Combine(d, "2"), "My Clip it's 100%.mp4", 3);
        var outs = RunOk("vid-extract-audio", IoPath.Combine(d, "o"), new[] { a, b }, ("format", "wav"));
        Assert.Equal(2, outs.Distinct().Count());
        Assert.InRange(Probe(outs[0]).Duration, 1.8, 2.2);
        Assert.InRange(Probe(outs[1]).Duration, 2.8, 3.2);
        var m1 = RunOk1("vid-mute", IoPath.Combine(d, "o"), new[] { a });
        var m2 = RunOk1("vid-mute", IoPath.Combine(d, "o"), new[] { b });
        Assert.NotEqual(m1, m2);
        Assert.Null(Probe(m1).AudioCodec);
        Assert.Null(Probe(m2).AudioCodec);
    }

    [Theory]
    [InlineData("avi")]
    [InlineData("wmv")]
    [InlineData("3gp")]
    [InlineData("mkv")]
    [InlineData("mov")]
    [InlineData("webm")]
    [InlineData("gif")]
    public void Convert_every_target_plays_back(string fmt)
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2, size: "322x182");
        var o = RunOk1("vid-convert", d, new[] { src }, ("format", fmt));
        Assert.EndsWith("." + fmt, o);
        var p = Probe(o);
        Assert.InRange(p.Duration, 1.5, 2.6);
        if (fmt != "gif") Assert.NotNull(p.AudioCodec);
        Assert.True(CountFrames(o) > 5);
    }

    [Fact]
    public void Convert_audio_only_input()
    {
        var d = NewDir();
        var v = MakeVideo(d, "s.mp4", 2, audio: false);
        var r = Run("vid-convert", d, new[] { v }, ("format", "mp3"));
        Assert.False(r.Ok);
        Assert.Empty(Directory.GetFiles(d, "*converted*"));
    }

    [Fact]
    public void Volume_zero_is_silent_on_avi_webm_mp4()
    {
        var d = NewDir();
        foreach (var ext in new[] { ".avi", ".webm", ".mp4" })
        {
            var src = MakeVideo(d, "a" + ext, 2);
            var o = RunOk1("vid-volume", d, new[] { src }, ("volume", "0"));
            Assert.True(MeanVolumeDb(o) < -60, ext);
            Assert.NotNull(Probe(o).VideoCodec);
        }
    }

    [Fact]
    public void Loop_keeps_audio_and_frames()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2);
        var o = RunOk1("vid-loop", d, new[] { src }, ("count", "3"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 5.8, 6.3);
        Assert.NotNull(p.AudioCodec);
        Assert.InRange(CountFrames(o), 88, 92);
    }

    [Fact]
    public void Reverse_audio_less_clip()
    {
        var d = NewDir();
        var src = MakeVideo(d, "a.mp4", 2, audio: false);
        var o = RunOk1("vid-reverse", d, new[] { src });
        Assert.Null(Probe(o).AudioCodec);
        Assert.InRange(CountFrames(o), 29, 31);
    }

    [Fact]
    public void Corrupt_and_zero_byte_inputs_fail_without_leaving_files()
    {
        var d = NewDir();
        var bad = IoPath.Combine(d, "bad.mp4");
        File.WriteAllBytes(bad, new byte[0]);
        var junk = IoPath.Combine(d, "junk.mp4");
        File.WriteAllBytes(junk, Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray());
        var good = MakeVideo(d, "good.mp4", 2);
        var cut = IoPath.Combine(d, "cut.mp4");
        File.WriteAllBytes(cut, File.ReadAllBytes(good).Take(3000).ToArray());
        var o = IoPath.Combine(d, "out");
        foreach (var id in new[] { "vid-mute", "vid-trim", "vid-convert", "vid-rotate", "vid-compress", "vid-volume", "vid-extract-audio", "vid-to-gif" })
            foreach (var f in new[] { bad, junk, cut })
            {
                var r = Run(id, o, new[] { f });
                Assert.False(r.Ok, id + " on " + IoPath.GetFileName(f));
            }
        Assert.True(!Directory.Exists(o) || Directory.GetFiles(o).Length == 0, string.Join(",", Directory.Exists(o) ? Directory.GetFiles(o) : new string[0]));
    }

    [Fact]
    public void Add_audio_replace_and_mix_with_odd_video()
    {
        var d = NewDir();
        var v = MakeVideo(d, "v.avi", 3, audio: false, size: "321x181");
        var a = MakeAudio(d, "a.wav", 1);
        var o = RunOk1("vid-add-audio", d, new[] { a, v });
        var p = Probe(o);
        Assert.InRange(p.Duration, 2.8, 3.3);
        Assert.NotNull(p.AudioCodec);
        var v2 = MakeVideo(d, "v2.mp4", 3);
        var o2 = RunOk1("vid-add-audio", d, new[] { v2, a }, ("mode", "mix"));
        Assert.InRange(Probe(o2).Duration, 2.8, 3.3);
    }

    [Fact]
    public void Gif_start_duration_frame_count()
    {
        var d = NewDir();
        var v = MakeVideo(d, "v.mp4", 4);
        var o = RunOk1("vid-to-gif", d, new[] { v }, ("start", "1"), ("duration", "2"), ("fps", "5"), ("width", "100"));
        Assert.Equal(100, Probe(o).Width);
        Assert.InRange(CountFrames(o), 9, 11);
    }

    [Fact]
    public void Compress_that_grows_fails_and_leaves_nothing()
    {
        var d = NewDir();
        var v = MakeVideo(d, "tiny.mp4", 1, size: "64x64", crf: 40);
        var o = IoPath.Combine(d, "out");
        var r = Run("vid-compress", o, new[] { v }, ("quality", "high"));
        if (r.Ok) Assert.True(new FileInfo(r.Outputs[0]).Length < new FileInfo(v).Length);
        else Assert.Empty(Directory.GetFiles(o));
    }

    [Fact]
    public void Hostile_resize_options_are_rejected_naming_the_option()
    {
        var d = NewDir();
        var v = MakeVideo(d, "v.mp4", 1);
        foreach (var c in new[] { "red", "#12", "0xff0000", "#ff0000'", "#GG0000", "#ff0000:x=1" })
        {
            var r = Run("vid-resize", d, new[] { v }, ("width", "100"), ("height", "100"), ("mode", "pad"), ("color", c));
            Assert.False(r.Ok, c);
            Assert.Contains("color", r.Error);
        }
        foreach (var (k, val) in new[] { ("width", "1e3"), ("width", "-2"), ("width", "7682"), ("height", "99999999999") })
        {
            var r = Run("vid-resize", d, new[] { v }, (k, val));
            Assert.False(r.Ok, val);
            Assert.Contains(k, r.Error);
        }
    }

    [Fact]
    public void Trim_delete_on_a_variable_frame_rate_clip_removes_the_right_amount()
    {
        var d = NewDir();
        var src = IoPath.Combine(d, "vfr.mp4");
        var (code, _, err) = Exec("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=160x90:rate=30,select=lt(mod(n\\,3)\\,2)",
            "-t", "4", "-fps_mode", "vfr", "-c:v", "libx264", "-pix_fmt", "yuv420p", src);
        Assert.True(code == 0, err);
        var o = RunOk1("vid-trim", d, new[] { src }, ("start", "1"), ("end", "2"), ("mode", "delete"));
        Assert.InRange(Probe(o).Duration, 2.7, 3.3);
    }

    [Fact]
    public void Cancel_before_start_leaves_nothing()
    {
        var d = NewDir();
        var v = MakeVideo(d, "v.mp4", 2);
        var tool = Registry.Find("vid-rotate")!;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var o = IoPath.Combine(d, "o");
        var r = ToolRunner.RunAsync(tool, new ToolRequest(new[] { v }, o, new Dictionary<string, string>()), null, cts.Token).GetAwaiter().GetResult();
        Assert.False(r.Ok);
        Assert.True(!Directory.Exists(o) || Directory.GetFiles(o).Length == 0);
    }
}
