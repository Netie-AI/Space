using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>
/// Each test drives a real tool on a real clip made with ffmpeg lavfi and reads the artifact back with ffprobe
/// (duration, size, streams, codec, frame count) or by comparing decoded frames.
/// </summary>
public class VideoToolTests
{
    private const double Tol = 0.35;

    private static readonly string[] AllIds =
    {
        "vid-trim", "vid-split", "vid-merge", "vid-mute", "vid-extract-audio", "vid-convert", "vid-compress", "vid-resize",
        "vid-crop", "vid-rotate", "vid-flip", "vid-speed", "vid-reverse", "vid-loop", "vid-volume", "vid-to-gif", "vid-add-audio",
    };

    // ---------------------------------------------------------------- registry

    [Fact]
    public void Exactly_the_built_video_tools_are_registered_and_available()
    {
        var registered = new VideoToolProvider().Runners().Select(r => r.ToolId).OrderBy(x => x).ToArray();
        Assert.Equal(AllIds.OrderBy(x => x).ToArray(), registered);
        foreach (var id in AllIds)
        {
            var t = Registry.Find(id)!;
            Assert.NotNull(t.Runner);
            Assert.True(t.Availability.Available, id + ": " + t.Availability.Text);
        }
    }

    [Fact]
    public void Tools_that_are_not_built_keep_saying_so()
    {
        foreach (var id in new[] { "vid-editor", "vid-stickers", "vid-meme", "vid-stop-motion" })
        {
            var t = Registry.Find(id)!;
            Assert.Null(t.Runner);
            Assert.False(t.Availability.Available);
            Assert.Equal(ToolAvailabilityRules.TextNotBuilt, t.Availability.Text);
        }
    }

    // ---------------------------------------------------------------- trim

    [Fact]
    public void Trim_extract_keeps_the_requested_window_with_picture_and_sound()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4);
        var before = IoFile.ReadAllBytes(input);
        var outDir = IoPath.Combine(dir, "out");

        var output = RunOk1("vid-trim", outDir, new[] { input }, ("start", "1"), ("end", "3"));

        var info = Probe(output);
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);
        Assert.Equal("h264", info.VideoCodec);
        Assert.NotNull(info.AudioCodec);
        Assert.Equal((320, 180), (info.Width, info.Height));
        // the first frame of the result is the source frame at 1 s (rate 15 -> frame 15), not frame 0
        var first = FrameGray(output, 0);
        Assert.True(MeanAbsDiff(first, FrameGray(input, 15)) < MeanAbsDiff(first, FrameGray(input, 0)));
        Assert.Equal(before, IoFile.ReadAllBytes(input));
    }

    [Fact]
    public void Trim_delete_mode_removes_the_middle()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4);
        var output = RunOk1("vid-trim", IoPath.Combine(dir, "out"), new[] { input }, ("start", "1"), ("end", "2"), ("mode", "delete"));
        var info = Probe(output);
        Assert.InRange(info.Duration, 3.0 - Tol, 3.0 + Tol);
        Assert.NotNull(info.AudioCodec);
        Assert.Equal((320, 180), (info.Width, info.Height));
    }

    [Fact]
    public void Trim_rejects_bad_times_naming_the_option()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var outDir = IoPath.Combine(dir, "out");
        var bad = Run("vid-trim", outDir, new[] { input }, ("start", "0;touch x"));
        Assert.False(bad.Ok);
        Assert.Contains("'start'", bad.Error);
        var order = Run("vid-trim", outDir, new[] { input }, ("start", "2"), ("end", "1"));
        Assert.False(order.Ok);
        Assert.Contains("'end'", order.Error);
        var past = Run("vid-trim", outDir, new[] { input }, ("start", "9"));
        Assert.False(past.Ok);
        Assert.Contains("'start'", past.Error);
        Assert.Empty(IoDirectory.Exists(outDir) ? IoDirectory.GetFiles(outDir) : Array.Empty<string>());
    }

    // ---------------------------------------------------------------- split

    [Fact]
    public void Split_into_equal_parts_gives_parts_that_add_up()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3);
        var outs = RunOk("vid-split", IoPath.Combine(dir, "out"), new[] { input }, ("parts", "3"));
        Assert.Equal(3, outs.Count);
        Assert.Equal(3, outs.Distinct().Count());
        double total = 0;
        foreach (var o in outs)
        {
            var info = Probe(o);
            Assert.InRange(info.Duration, 1.0 - Tol, 1.0 + Tol);
            Assert.NotNull(info.AudioCodec);
            total += info.Duration;
        }
        Assert.InRange(total, 3.0 - 0.5, 3.0 + 0.5);
    }

    [Fact]
    public void Split_at_cut_points_uses_them()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4);
        var outs = RunOk("vid-split", IoPath.Combine(dir, "out"), new[] { input }, ("at", "1, 0:03"));
        Assert.Equal(3, outs.Count);
        var d = outs.Select(o => Probe(o).Duration).ToArray();
        Assert.InRange(d[0], 1.0 - Tol, 1.0 + Tol);
        Assert.InRange(d[1], 2.0 - Tol, 2.0 + Tol);
        Assert.InRange(d[2], 1.0 - Tol, 1.0 + Tol);
        var bad = Run("vid-split", IoPath.Combine(dir, "out2"), new[] { input }, ("at", "1,99"));
        Assert.False(bad.Ok);
        Assert.Contains("'at'", bad.Error);
    }

    // ---------------------------------------------------------------- merge

    [Fact]
    public void Merge_joins_clips_of_different_size_and_audio_presence()
    {
        var dir = NewDir();
        var a = MakeVideo(dir, "a.mp4", seconds: 2);
        var b = MakeVideo(dir, "b.mp4", seconds: 2, audio: false, size: "160x90");
        var output = RunOk1("vid-merge", IoPath.Combine(dir, "out"), new[] { a, b });
        var info = Probe(output);
        Assert.InRange(info.Duration, 4.0 - Tol, 4.0 + Tol);
        Assert.Equal((320, 180), (info.Width, info.Height));
        Assert.Equal("h264", info.VideoCodec);
        Assert.NotNull(info.AudioCodec);
        Assert.InRange(CountFrames(output), 55, 65);
    }

    [Fact]
    public void Merge_refuses_images_and_a_single_file_without_pretending()
    {
        var dir = NewDir();
        var a = MakeVideo(dir, "a.mp4", seconds: 1);
        var single = Run("vid-merge", IoPath.Combine(dir, "out"), new[] { a });
        Assert.False(single.Ok);
        Assert.Contains("at least 2", single.Error);
        var png = IoPath.Combine(dir, "p.png");
        MustMakePng(png);
        var img = Run("vid-merge", IoPath.Combine(dir, "out"), new[] { a, png });
        Assert.False(img.Ok);
        Assert.Contains("not built in this version", img.Error);
    }

    private static void MustMakePng(string path)
    {
        var (code, _, err) = Exec("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=1", "-frames:v", "1", path);
        Assert.True(code == 0, err);
    }

    // ---------------------------------------------------------------- mute

    [Fact]
    public void Mute_drops_audio_and_leaves_the_picture_untouched()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3);
        var output = RunOk1("vid-mute", IoPath.Combine(dir, "out"), new[] { input });
        var info = Probe(output);
        Assert.Null(info.AudioCodec);
        Assert.Equal("h264", info.VideoCodec);
        Assert.InRange(info.Duration, 3.0 - Tol, 3.0 + Tol);
        Assert.Equal(CountFrames(input), CountFrames(output));
        var silent = MakeVideo(dir, "silent.mp4", seconds: 1, audio: false);
        var again = Run("vid-mute", IoPath.Combine(dir, "out"), new[] { silent });
        Assert.False(again.Ok);
        Assert.Contains("no audio", again.Error);
    }

    // ---------------------------------------------------------------- extract audio

    [Theory]
    [InlineData("mp3", "mp3")]
    [InlineData("wav", "pcm_s16le")]
    [InlineData("m4a", "aac")]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "vorbis")]
    public void Extract_audio_makes_an_audio_only_file_in_the_chosen_format(string format, string codec)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3);
        var output = RunOk1("vid-extract-audio", IoPath.Combine(dir, "out"), new[] { input }, ("format", format));
        Assert.EndsWith("." + format, output);
        var info = Probe(output);
        Assert.Null(info.VideoCodec);
        Assert.Equal(codec, info.AudioCodec);
        Assert.InRange(info.Duration, 3.0 - Tol, 3.0 + Tol);
        Assert.True(MeanVolumeDb(output) > -60, "extracted audio should not be silent");
    }

    [Fact]
    public void Extract_audio_handles_several_files_and_refuses_a_silent_one()
    {
        var dir = NewDir();
        var a = MakeVideo(dir, "a.mp4", seconds: 1);
        var b = MakeVideo(dir, "b.mkv", seconds: 2);
        var outs = RunOk("vid-extract-audio", IoPath.Combine(dir, "out"), new[] { a, b });
        Assert.Equal(2, outs.Count);
        var silent = MakeVideo(dir, "s.mp4", seconds: 1, audio: false);
        var r = Run("vid-extract-audio", IoPath.Combine(dir, "out2"), new[] { silent });
        Assert.False(r.Ok);
        Assert.Contains("no audio", r.Error);
    }

    // ---------------------------------------------------------------- convert

    [Theory]
    [InlineData("mp4", "h264", "aac")]
    [InlineData("mov", "h264", "aac")]
    [InlineData("mkv", "h264", "aac")]
    [InlineData("avi", "h264", "mp3")]
    [InlineData("webm", "vp9", "opus")]
    [InlineData("wmv", "wmv2", "wmav2")]
    [InlineData("3gp", "h264", "aac")]
    public void Convert_to_each_video_format_has_that_container_and_codecs(string format, string vcodec, string acodec)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.avi", seconds: 2);
        var output = RunOk1("vid-convert", IoPath.Combine(dir, "out"), new[] { input }, ("format", format));
        Assert.EndsWith("." + format, output);
        var info = Probe(output);
        Assert.Equal(vcodec, info.VideoCodec);
        Assert.Equal(acodec, info.AudioCodec);
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);
        Assert.Equal((320, 180), (info.Width, info.Height));
    }

    [Fact]
    public void Convert_to_gif_and_to_audio_formats()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var gif = RunOk1("vid-convert", IoPath.Combine(dir, "out"), new[] { input }, ("format", "gif"));
        var g = Probe(gif);
        Assert.Equal("gif", g.VideoCodec);
        Assert.Null(g.AudioCodec);
        Assert.InRange(CountFrames(gif), 20, 28); // 12 fps * 2 s
        var wav = RunOk1("vid-convert", IoPath.Combine(dir, "out"), new[] { input }, ("format", "wav"));
        Assert.Equal("pcm_s16le", Probe(wav).AudioCodec);
        Assert.Null(Probe(wav).VideoCodec);
    }

    // ---------------------------------------------------------------- compress

    [Fact]
    public void Compress_makes_a_smaller_file_of_the_same_length_and_can_cap_the_height()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, size: "960x540", crf: 4);
        var output = RunOk1("vid-compress", IoPath.Combine(dir, "out"), new[] { input });
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length);
        var info = Probe(output);
        Assert.InRange(info.Duration, 3.0 - Tol, 3.0 + Tol);
        Assert.Equal((960, 540), (info.Width, info.Height));

        var small = RunOk1("vid-compress", IoPath.Combine(dir, "out"), new[] { input }, ("height", "360"), ("quality", "low"));
        var sInfo = Probe(small);
        Assert.Equal(360, sInfo.Height);
        Assert.Equal(640, sInfo.Width);
        Assert.True(new FileInfo(small).Length < new FileInfo(output).Length);
    }

    [Fact]
    public void Compress_that_would_grow_the_file_fails_and_leaves_nothing_behind()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "tiny.mp4", seconds: 2, size: "128x72", crf: 51);
        var outDir = IoPath.Combine(dir, "out");
        var r = Run("vid-compress", outDir, new[] { input }, ("quality", "high"));
        Assert.False(r.Ok);
        Assert.Contains("smaller", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    // ---------------------------------------------------------------- resize

    [Theory]
    [InlineData("stretch", "160", "90", 160, 90)]
    [InlineData("pad", "400", "400", 400, 400)]
    [InlineData("crop", "100", "100", 100, 100)]
    [InlineData("fit", "200", "200", 200, 112)]
    [InlineData("fit", "160", "0", 160, 90)]
    [InlineData("fit", "0", "90", 160, 90)]
    public void Resize_gives_the_requested_frame_size(string mode, string w, string h, int ew, int eh)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-resize", IoPath.Combine(dir, "out"), new[] { input }, ("width", w), ("height", h), ("mode", mode));
        var info = Probe(output);
        Assert.Equal((ew, eh), (info.Width, info.Height));
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);
        Assert.NotNull(info.AudioCodec);
    }

    [Fact]
    public void Resize_pad_uses_the_chosen_colour_and_rejects_bad_sizes()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 1);
        var outDir = IoPath.Combine(dir, "out");
        var output = RunOk1("vid-resize", outDir, new[] { input }, ("width", "320"), ("height", "320"), ("mode", "pad"), ("color", "#FFFFFF"));
        // the top rows are padding: white
        var (_, raw, _) = Exec("ffmpeg", "-v", "error", "-i", output, "-vf", "crop=320:20:0:0,format=gray", "-frames:v", "1", "-f", "rawvideo", "-");
        Assert.True(raw.Length > 0 && raw.Average(b => (double)b) > 225, "padding should be white");
        var odd = Run("vid-resize", outDir, new[] { input }, ("width", "161"));
        Assert.False(odd.Ok);
        Assert.Contains("'width'", odd.Error);
        var none = Run("vid-resize", outDir, new[] { input });
        Assert.False(none.Ok);
        Assert.Contains("'width'", none.Error);
    }

    // ---------------------------------------------------------------- crop

    [Fact]
    public void Crop_returns_exactly_the_chosen_region()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-crop", IoPath.Combine(dir, "out"), new[] { input },
            ("x", "80"), ("y", "40"), ("width", "160"), ("height", "90"));
        var info = Probe(output);
        Assert.Equal((160, 90), (info.Width, info.Height));
        var reference = FrameGray(input, 0, "crop=160:90:80:40");
        var wrongSpot = FrameGray(input, 0, "crop=160:90:0:0");
        var got = FrameGray(output, 0);
        Assert.True(MeanAbsDiff(got, reference) < 6, "cropped picture should match the reference region");
        Assert.True(MeanAbsDiff(got, reference) < MeanAbsDiff(got, wrongSpot));
    }

    [Fact]
    public void Crop_outside_the_frame_or_missing_size_is_refused()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 1);
        var outDir = IoPath.Combine(dir, "out");
        var outside = Run("vid-crop", outDir, new[] { input }, ("x", "200"), ("width", "160"), ("height", "90"));
        Assert.False(outside.Ok);
        Assert.Contains("does not fit", outside.Error);
        var missing = Run("vid-crop", outDir, new[] { input }, ("height", "90"));
        Assert.False(missing.Ok);
        Assert.Contains("'width'", missing.Error);
    }

    // ---------------------------------------------------------------- rotate / flip

    [Theory]
    [InlineData("90", "transpose=1", 180, 320)]
    [InlineData("270", "transpose=2", 180, 320)]
    [InlineData("180", "hflip,vflip", 320, 180)]
    public void Rotate_turns_the_picture_the_right_way(string angle, string reference, int ew, int eh)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-rotate", IoPath.Combine(dir, "out"), new[] { input }, ("angle", angle));
        var info = Probe(output);
        Assert.Equal((ew, eh), (info.Width, info.Height));
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);
        var got = FrameGray(output, 3);
        Assert.True(MeanAbsDiff(got, FrameGray(input, 3, reference)) < 6, "picture should match the reference rotation");
        Assert.True(MeanAbsDiff(got, FrameGray(input, 3, angle == "90" ? "transpose=2" : "transpose=1")) > 6,
            "and not the opposite rotation");
    }

    [Theory]
    [InlineData("horizontal", "hflip", "vflip")]
    [InlineData("vertical", "vflip", "hflip")]
    public void Flip_mirrors_the_picture_on_the_chosen_axis(string axis, string reference, string other)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-flip", IoPath.Combine(dir, "out"), new[] { input }, ("axis", axis));
        Assert.Equal((320, 180), (Probe(output).Width, Probe(output).Height));
        var got = FrameGray(output, 3);
        Assert.True(MeanAbsDiff(got, FrameGray(input, 3, reference)) < 6);
        Assert.True(MeanAbsDiff(got, FrameGray(input, 3, other)) > 6);
        Assert.True(MeanAbsDiff(got, FrameGray(input, 3)) > 6);
    }

    // ---------------------------------------------------------------- speed

    [Theory]
    [InlineData("200", 2.0)]
    [InlineData("400", 1.0)]
    [InlineData("50", 8.0)]
    public void Speed_scales_the_duration_of_picture_and_sound(string speed, double expected)
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4);
        var output = RunOk1("vid-speed", IoPath.Combine(dir, "out"), new[] { input }, ("speed", speed));
        var info = Probe(output);
        Assert.InRange(info.Duration, expected - 0.4, expected + 0.4);
        Assert.NotNull(info.AudioCodec);
    }

    [Fact]
    public void Speed_can_drop_the_audio_and_chains_atempo_in_range()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-speed", IoPath.Combine(dir, "out"), new[] { input }, ("speed", "200"), ("audio", "mute"));
        Assert.Null(Probe(output).AudioCodec);
        Assert.Equal("atempo=2.0,atempo=2", VideoToolProvider.AtempoChain(4));
        Assert.Equal("atempo=0.5,atempo=0.5", VideoToolProvider.AtempoChain(0.25));
        Assert.Equal("atempo=1.5", VideoToolProvider.AtempoChain(1.5));
    }

    // ---------------------------------------------------------------- reverse

    [Fact]
    public void Reverse_plays_the_frames_backwards()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-reverse", IoPath.Combine(dir, "out"), new[] { input });
        var info = Probe(output);
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);
        Assert.NotNull(info.AudioCodec);
        var frames = CountFrames(input);
        var first = FrameGray(output, 0);
        Assert.True(MeanAbsDiff(first, FrameGray(input, frames - 1)) < MeanAbsDiff(first, FrameGray(input, 0)));
        var last = FrameGray(output, frames - 1);
        Assert.True(MeanAbsDiff(last, FrameGray(input, 0)) < MeanAbsDiff(last, FrameGray(input, frames - 1)));
    }

    [Fact]
    public void Reverse_refuses_a_video_too_long_to_hold_in_memory()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "long.mp4", seconds: 61, audio: false, size: "64x64", rate: 1);
        var r = Run("vid-reverse", IoPath.Combine(dir, "out"), new[] { input });
        Assert.False(r.Ok);
        Assert.Contains("not built in this version", r.Error);
    }

    // ---------------------------------------------------------------- loop

    [Fact]
    public void Loop_repeats_the_clip_the_requested_number_of_times()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var output = RunOk1("vid-loop", IoPath.Combine(dir, "out"), new[] { input }, ("count", "3"));
        var info = Probe(output);
        Assert.InRange(info.Duration, 6.0 - Tol, 6.0 + Tol);
        Assert.NotNull(info.AudioCodec);
        Assert.InRange(CountFrames(output), 85, 95); // 3 * 30 frames
    }

    // ---------------------------------------------------------------- volume

    [Fact]
    public void Volume_changes_the_loudness_by_the_requested_amount_and_keeps_the_picture_stream()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var baseline = MeanVolumeDb(input);
        var outDir = IoPath.Combine(dir, "out");
        var half = RunOk1("vid-volume", outDir, new[] { input }, ("volume", "50"));
        Assert.InRange(MeanVolumeDb(half), baseline - 7.5, baseline - 4.5);
        Assert.Equal("h264", Probe(half).VideoCodec);
        Assert.Equal(CountFrames(input), CountFrames(half));
        var loud = RunOk1("vid-volume", outDir, new[] { input }, ("volume", "200"));
        Assert.InRange(MeanVolumeDb(loud), baseline + 4.5, baseline + 7.5);
        var zero = RunOk1("vid-volume", outDir, new[] { input }, ("volume", "0"));
        Assert.True(MeanVolumeDb(zero) < baseline - 40);
        Assert.InRange(Probe(half).Duration, 2.0 - Tol, 2.0 + Tol);
    }

    // ---------------------------------------------------------------- to gif

    [Fact]
    public void ToGif_makes_an_animated_gif_with_the_requested_fps_size_and_window()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4);
        var output = RunOk1("vid-to-gif", IoPath.Combine(dir, "out"), new[] { input },
            ("fps", "10"), ("width", "160"), ("start", "1"), ("duration", "2"));
        var info = Probe(output);
        Assert.Equal("gif", info.VideoCodec);
        Assert.Null(info.AudioCodec);
        Assert.Equal((160, 90), (info.Width, info.Height));
        Assert.InRange(CountFrames(output), 17, 23); // 10 fps * 2 s
        Assert.InRange(info.Duration, 2.0 - 0.4, 2.0 + 0.4);
    }

    [Fact]
    public void ToGif_never_upscales_and_refuses_images()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 1);
        var output = RunOk1("vid-to-gif", IoPath.Combine(dir, "out"), new[] { input }, ("width", "1000"));
        Assert.Equal(320, Probe(output).Width);
        var png = IoPath.Combine(dir, "p.png");
        MustMakePng(png);
        var r = Run("vid-to-gif", IoPath.Combine(dir, "out"), new[] { png });
        Assert.False(r.Ok);
        Assert.Contains("not built in this version", r.Error);
    }

    // ---------------------------------------------------------------- add audio

    [Fact]
    public void AddAudio_replace_puts_the_new_sound_on_the_video_and_keeps_the_video_length()
    {
        var dir = NewDir();
        var video = MakeVideo(dir, "clip.mp4", seconds: 3); // carries a 440 Hz tone
        var tone = MakeAudio(dir, "tone.wav", seconds: 5);
        var outDir = IoPath.Combine(dir, "out");

        var output = RunOk1("vid-add-audio", outDir, new[] { video, tone });
        var info = Probe(output);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.InRange(info.Duration, 3.0 - Tol, 3.0 + Tol); // the 5 s tone is cut to the video
        Assert.Equal(CountFrames(video), CountFrames(output));

        // replacing with a silent (volume 0) track proves the original sound is gone
        var silent = RunOk1("vid-add-audio", outDir, new[] { tone, video }, ("volume", "0")); // order does not matter
        Assert.True(MeanVolumeDb(silent) < MeanVolumeDb(video) - 40);
    }

    [Fact]
    public void AddAudio_mix_keeps_both_tracks_and_works_on_a_silent_video()
    {
        var dir = NewDir();
        var video = MakeVideo(dir, "clip.mp4", seconds: 2);
        var tone = MakeAudio(dir, "tone.mp3", seconds: 2);
        var outDir = IoPath.Combine(dir, "out");
        var mixed = RunOk1("vid-add-audio", outDir, new[] { video, tone }, ("mode", "mix"));
        Assert.NotNull(Probe(mixed).AudioCodec);
        Assert.True(MeanVolumeDb(mixed) > -60);

        var silentVideo = MakeVideo(dir, "mute.mp4", seconds: 2, audio: false);
        var added = RunOk1("vid-add-audio", outDir, new[] { silentVideo, tone }, ("mode", "mix"));
        var info = Probe(added);
        Assert.NotNull(info.AudioCodec);
        Assert.InRange(info.Duration, 2.0 - Tol, 2.0 + Tol);

        var notAudio = Run("vid-add-audio", outDir, new[] { video, silentVideo });
        Assert.False(notAudio.Ok);
        Assert.Contains("no audio", notAudio.Error);
    }

    // ---------------------------------------------------------------- cross-cutting

    [Fact]
    public void Same_file_name_from_two_folders_never_overwrites_an_output()
    {
        var root = NewDir();
        var one = MakeVideo(IoPath.Combine(root, "a"), "clip.mp4", seconds: 1);
        var two = MakeVideo(IoPath.Combine(root, "b"), "clip.mp4", seconds: 3);
        var outDir = IoPath.Combine(root, "out");
        var outs = RunOk("vid-extract-audio", outDir, new[] { one, two }, ("format", "wav"));
        Assert.Equal(2, outs.Count);
        Assert.Equal(2, outs.Select(o => o.ToLowerInvariant()).Distinct().Count());
        Assert.InRange(Probe(outs[0]).Duration, 1.0 - Tol, 1.0 + Tol);
        Assert.InRange(Probe(outs[1]).Duration, 3.0 - Tol, 3.0 + Tol);
        // and a second run into the same folder keeps the first run's files
        var again = RunOk("vid-extract-audio", outDir, new[] { one }, ("format", "wav"));
        Assert.DoesNotContain(again[0], outs);
        Assert.Equal(3, IoDirectory.GetFiles(outDir).Length);
    }

    [Fact]
    public void Output_goes_to_the_out_dir_even_when_it_is_the_input_folder_and_the_input_survives()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 1);
        var before = IoFile.ReadAllBytes(input);
        var outs = RunOk("vid-rotate", dir, new[] { input });
        Assert.NotEqual(IoPath.GetFullPath(input), IoPath.GetFullPath(outs[0]));
        Assert.Equal(before, IoFile.ReadAllBytes(input));
        Assert.Equal(IoPath.GetFullPath(dir), IoPath.GetDirectoryName(IoPath.GetFullPath(outs[0])));
    }

    [Fact]
    public void Hostile_file_names_are_passed_as_plain_arguments()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "-weird name'; echo $(id) & [x],.mp4", seconds: 1);
        var output = RunOk1("vid-flip", IoPath.Combine(dir, "out dir"), new[] { input });
        Assert.InRange(Probe(output).Duration, 1.0 - Tol, 1.0 + Tol);
        Assert.False(IoFile.Exists(IoPath.Combine(dir, "x")));
    }

    [Fact]
    public void Text_options_cannot_inject_into_a_filter_graph()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 2);
        var outDir = IoPath.Combine(dir, "out");
        foreach (var evil in new[] { "1,drawtext=text=x", "1[a];[a]hflip", "1:00:00:00", "-1", "1e3x", "0.5;1" })
        {
            var r = Run("vid-to-gif", outDir, new[] { input }, ("duration", evil));
            Assert.False(r.Ok, "should reject '" + evil + "'");
            Assert.Contains("'duration'", r.Error);
        }
        var color = Run("vid-resize", outDir, new[] { input }, ("width", "160"), ("height", "90"), ("mode", "pad"), ("color", "#00000;drawtext"));
        Assert.False(color.Ok);
        Assert.Contains("'color'", color.Error);
        Assert.Empty(IoDirectory.Exists(outDir) ? IoDirectory.GetFiles(outDir) : Array.Empty<string>());
    }

    [Fact]
    public async Task Progress_is_reported_and_stays_below_done_until_the_runner_finishes()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3);
        var tool = Registry.Find("vid-rotate")!;
        var progress = new ListProgress();
        var req = new ToolRequest(new[] { input }, IoPath.Combine(dir, "out"), new Dictionary<string, string>());
        var r = await ToolRunner.RunAsync(tool, req, progress, CancellationToken.None);
        Assert.True(r.Ok, r.Error);
        Assert.NotEmpty(progress.Reports);
        Assert.All(progress.Reports.Take(progress.Reports.Count - 1), p => Assert.InRange(p.Percent, 0, 99));
        Assert.Equal(100, progress.Reports[^1].Percent);
    }

    [Fact]
    public async Task Cancel_stops_ffmpeg_and_leaves_no_partial_file()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "slow.webm", seconds: 6, size: "640x360", rate: 25);
        var outDir = IoPath.Combine(dir, "out");
        var tool = Registry.Find("vid-loop")!;
        var req = new ToolRequest(new[] { input }, outDir, new Dictionary<string, string> { ["count"] = "16" });
        using var cts = new CancellationTokenSource();
        var task = ToolRunner.RunAsync(tool, req, null, cts.Token);
        await Task.Delay(400);
        cts.Cancel();
        var r = await task;
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Broken_input_fails_with_ffprobe_or_ffmpeg_text_and_no_output()
    {
        var dir = NewDir();
        var junk = IoPath.Combine(dir, "junk.mp4");
        IoFile.WriteAllText(junk, "this is not a video");
        var outDir = IoPath.Combine(dir, "out");
        var r = Run("vid-rotate", outDir, new[] { junk });
        Assert.False(r.Ok);
        Assert.Contains("ffprobe", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }
}
