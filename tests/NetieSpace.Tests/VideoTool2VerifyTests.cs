using System.Globalization;
using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>Adversarial checks of the second-wave video runners, written by the verifier.</summary>
public class VideoTool2VerifyTests
{
    private static string Dir(string root, string name)
    {
        var d = IoPath.Combine(root, name);
        IoDirectory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Text_hostile_content_odd_size_hostile_names()
    {
        var root = NewDir();
        var d = Dir(root, "it's a dïr %x");
        var clip = MakeFlat(d, "cl'ip ü; [1].mp4", seconds: 2, size: "321x181", audio: false);
        var o = RunOk1("vid-add-text", Out(root), new[] { clip },
            ("text", "a'b:c%{pts}\\n,;[x]\"q\" \\ drawbox=w=999:h=999:color=red:t=fill"), ("position", "middle-center"), ("size", "30"));
        var p = Probe(o);
        Assert.Equal(320, p.Width);
        Assert.InRange(p.Duration, 1.8, 2.2);
        var f = FrameAt(o, 5);
        // the far corner is untouched (no injected red box)
        var m = f.Mean(0, 0, 20, 20);
        Assert.True(Math.Abs(m.G - m.R) < 6, "corner coloured: " + m);
    }

    [Fact]
    public void Text_window_only_between_start_and_end()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "c.mp4", seconds: 3, audio: false);
        var o = RunOk1("vid-add-text", Out(root), new[] { clip }, ("text", "HELLO"), ("position", "top-left"), ("size", "40"), ("start", "1"), ("end", "2"));
        Assert.True(FrameAt(o, 3).Diff(FrameAt(clip, 3), 0, 0, 160, 60) < 0.8);
        Assert.True(FrameAt(o, 22).Diff(FrameAt(clip, 22), 0, 0, 160, 60) > 3);
        Assert.True(FrameAt(o, 40).Diff(FrameAt(clip, 40), 0, 0, 160, 60) < 0.8);
    }

    [Fact]
    public void Text_unicode_and_blank_lines()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "c.mp4", seconds: 1, audio: true);
        var o = RunOk1("vid-add-text", Out(root), new[] { clip }, ("text", "Line one\n\nLine three éè"), ("size", "20"), ("position", "top-center"));
        Assert.NotNull(Probe(o).AudioCodec);
        Assert.True(FrameAt(o, 3).Diff(FrameAt(clip, 3), 60, 0, 200, 70) > 1);
    }

    [Fact]
    public void Text_rejects_bad_values()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "c.mp4", seconds: 1, audio: false);
        Assert.False(Run("vid-add-text", Out(root), new[] { clip }, ("text", "x"), ("start", "5")).Ok);
        Assert.False(Run("vid-add-text", Out(root), new[] { clip }, ("text", "x"), ("start", "0.5"), ("end", "0.2")).Ok);
        Assert.False(Run("vid-add-text", Out(root), new[] { clip }, ("text", "x"), ("color", "red")).Ok);
        Assert.False(Run("vid-add-text", Out(root), new[] { clip }, ("text", "   ")).Ok);
        Assert.False(Run("vid-add-text", Out(root), new[] { clip }, ("text", new string('a', 1001))).Ok);
        Assert.Empty(Files(Out(root)));
    }

    [Fact]
    public void Corrupt_and_zero_byte_input_fail_without_output()
    {
        var root = NewDir();
        var z = IoPath.Combine(root, "z.mp4"); IoFile.WriteAllBytes(z, Array.Empty<byte>());
        var c = IoPath.Combine(root, "c.mp4"); IoFile.WriteAllBytes(c, new byte[4000]);
        foreach (var id in new[] { "vid-add-text", "vid-adjust", "vid-filters", "vid-stabilize", "vid-gif-editor", "vid-remove-logo" })
            foreach (var f in new[] { z, c })
            {
                var r = Run(id, Out(root), new[] { f }, ("text", "x"), ("x", "5"), ("y", "5"), ("width", "10"), ("height", "10"), ("brightness", "10"));
                Assert.False(r.Ok, id);
            }
        Assert.Empty(Files(Out(root)));
    }

    [Fact]
    public void Image_opacity_portrait_odd_video_same_names()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "c.mp4", seconds: 2, size: "321x181", audio: false, color: "black");
        var img = MakeImage(root, "c.png", "color=c=white:s=40x200");
        var o = RunOk1("vid-add-image", Out(root), new[] { clip, img }, ("position", "middle-center"), ("scale", "10"), ("opacity", "50"));
        var f = FrameAt(o, 4);
        var m = f.MeanLuma(154, 85, 10, 10);
        Assert.InRange(m, 90, 165);
        Assert.True(f.MeanLuma(0, 0, 20, 20) < 10);
        Assert.InRange(Probe(o).Duration, 1.8, 2.2);
    }

    [Fact]
    public void Image_window_and_jpg_input_order_swapped()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "c.mp4", seconds: 3, audio: true, color: "black");
        var img = MakeImage(root, "l.jpg", "color=c=white:s=64x64");
        var o = RunOk1("vid-add-image", Out(root), new[] { img, clip }, ("position", "bottom-right"), ("scale", "30"), ("start", "1"), ("end", "2"));
        Assert.True(FrameAt(o, 3).MeanLuma(260, 130, 20, 20) < 10);
        Assert.True(FrameAt(o, 22).MeanLuma(260, 130, 20, 20) > 200);
        Assert.True(FrameAt(o, 40).MeanLuma(260, 130, 20, 20) < 10);
        Assert.NotNull(Probe(o).AudioCodec);
    }

    [Fact]
    public void Image_rejects_two_videos_and_two_images()
    {
        var root = NewDir();
        var a = MakeFlat(root, "a.mp4", seconds: 1, audio: false);
        var b = MakeFlat(root, "b.mp4", seconds: 1, audio: false);
        var i1 = MakeImage(root, "i1.png", "color=c=red:s=16x16");
        var i2 = MakeImage(root, "i2.png", "color=c=red:s=16x16");
        Assert.False(Run("vid-add-image", Out(root), new[] { a, b }).Ok);
        Assert.False(Run("vid-add-image", Out(root), new[] { i1, i2 }).Ok);
        Assert.Empty(Files(Out(root)));
    }

    [Fact]
    public void Subtitles_apostrophe_dirs_vtt_top_position_and_colour()
    {
        var root = NewDir();
        var d = Dir(root, "it's [a] dir, x;y");
        var clip = MakeFlat(d, "v'id.mp4", seconds: 4, audio: false, color: "black");
        var vtt = IoPath.Combine(d, "s'ub.vtt");
        IoFile.WriteAllText(vtt, "WEBVTT\n\n00:00:01.000 --> 00:00:02.500\nHello there\n\n");
        var o = RunOk1("vid-subtitles", Out(root), new[] { clip }, ("subtitles", vtt), ("position", "top-center"), ("size", "15"), ("color", "#FF0000"));
        Assert.True(FrameAt(o, 3).MeanLuma(0, 0, 320, 180) < 0.5);
        var on = FrameAt(o, 30);
        Assert.True(on.MeanLuma(0, 0, 320, 60) > 0.5, "cue not visible at top");
        Assert.True(on.MeanLuma(0, 120, 320, 60) < 0.5, "cue leaked to the bottom");
        var (r, g, b) = on.Mean(0, 0, 320, 60);
        Assert.True(r > g * 3 && r > b * 3);
        Assert.True(FrameAt(o, 50).MeanLuma(0, 0, 320, 180) < 0.5);
    }

    [Fact]
    public void Subtitles_srt_with_utf8_bom_and_crlf_and_odd_size()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "v.mp4", seconds: 3, size: "321x181", audio: true, color: "black");
        var srt = IoPath.Combine(root, "s.srt");
        IoFile.WriteAllBytes(srt, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("1\r\n00:00:00,500 --> 00:00:02,000\r\nCafé it's\r\n\r\n")).ToArray());
        var o = RunOk1("vid-subtitles", Out(root), new[] { clip, srt });
        Assert.True(FrameAt(o, 10).MeanLuma(0, 120, 320, 60) > 0.5);
        Assert.True(FrameAt(o, 40).MeanLuma(0, 0, 320, 180) < 0.5);
    }

    [Fact]
    public void Subtitles_rejects_garbage_subtitle_and_no_leak()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "v.mp4", seconds: 1, audio: false);
        var srt = IoPath.Combine(root, "s.srt");
        IoFile.WriteAllText(srt, "this is not a subtitle");
        Assert.False(Run("vid-subtitles", Out(root), new[] { clip, srt }).Ok);
        var empty = IoPath.Combine(root, "e.srt"); IoFile.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.False(Run("vid-subtitles", Out(root), new[] { clip, empty }).Ok);
        Assert.Empty(Files(Out(root)));
    }

    [Fact]
    public void Audio_join_mono_and_stereo_same_names_in_different_folders()
    {
        var root = NewDir();
        var a = MakeAudio(Dir(root, "one"), "same.wav", 2, 440);
        var b2 = IoPath.Combine(Dir(root, "two"), "same.wav");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=22050", "-ac", "1", "-t", "3", b2);
        var outDir = IoPath.Combine(root, "new", "deeper");
        var o = RunOk1("vid-audio-cut-merge", outDir, new[] { a, b2 }, ("format", "wav"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 4.95, 5.05);
        Assert.StartsWith(IoPath.GetFullPath(outDir), o);
    }

    [Fact]
    public void Audio_cut_precise_with_fade_and_end_past_total()
    {
        var root = NewDir();
        var a = MakeAudio(root, "t.wav", 6, 440);
        Assert.False(Run("vid-audio-cut-merge", Out(root), new[] { a }, ("start", "6")).Ok);
        var o2 = RunOk1("vid-audio-cut-merge", Out(root), new[] { a }, ("start", "2"), ("end", "100"), ("format", "flac"), ("fade", "1"));
        Assert.InRange(Probe(o2).Duration, 3.95, 4.05);
        var o3 = RunOk1("vid-audio-cut-merge", Out(root), new[] { a }, ("start", "1.5"), ("end", "2.5"), ("format", "m4a"));
        Assert.InRange(Probe(o3).Duration, 0.9, 1.1);
    }

    [Fact]
    public void Audio_rejects_video_input_and_no_audio_stream()
    {
        var root = NewDir();
        var v = MakeFlat(root, "v.mp4", seconds: 1, audio: true);
        Assert.False(Run("vid-audio-cut-merge", Out(root), new[] { v }, ("start", "0.2")).Ok);
        var mp4 = IoPath.Combine(root, "s0.mp4");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=64x64:r=5", "-t", "1", "-c:v", "libx264", mp4);
        var silent = IoPath.Combine(root, "s.m4a");
        IoFile.Move(mp4, silent);
        Assert.False(Run("vid-audio-cut-merge", Out(root), new[] { silent }, ("start", "0.2")).Ok);
    }

    [Fact]
    public void Adjust_limits_odd_size_webm_no_audio()
    {
        var root = NewDir();
        var clip = MakeVideo(root, "w.webm", seconds: 2, audio: false, size: "321x181");
        foreach (var (k, v) in new[] { ("brightness", "100"), ("brightness", "-100"), ("contrast", "-100"), ("contrast", "100"), ("saturation", "100"), ("hue", "180"), ("hue", "-180") })
        {
            var o = RunOk1("vid-adjust", Out(root), new[] { clip }, (k, v));
            var p = Probe(o);
            Assert.Equal("vp9", p.VideoCodec);
            Assert.Equal(320, p.Width);
        }
        Assert.False(Run("vid-adjust", Out(root), new[] { clip }, ("brightness", "101")).Ok);
        Assert.False(Run("vid-adjust", Out(root), new[] { clip }, ("hue", "x")).Ok);
    }

    [Fact]
    public void Adjust_brightness_contrast_saturation_move_the_right_way()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "g.mp4", seconds: 1, audio: false, color: "gray");
        var up = RunOk1("vid-adjust", Out(root), new[] { clip }, ("brightness", "40"));
        var dn = RunOk1("vid-adjust", Out(root), new[] { clip }, ("brightness", "-40"));
        var l0 = FrameAt(clip, 3).MeanLuma(0, 0, 320, 180);
        Assert.True(FrameAt(up, 3).MeanLuma(0, 0, 320, 180) > l0 + 20);
        Assert.True(FrameAt(dn, 3).MeanLuma(0, 0, 320, 180) < l0 - 20);
        var two = MakeLavfi(root, "two.mp4", "color=c=0x909090:s=320x180:r=15,drawbox=x=0:y=0:w=160:h=180:color=0x707070:t=fill", seconds: 1);
        var hc = RunOk1("vid-adjust", Out(root), new[] { two }, ("contrast", "80"));
        var f = FrameAt(hc, 3);
        Assert.True(f.MeanLuma(200, 50, 50, 50) - f.MeanLuma(50, 50, 50, 50) > 35);
        var sat = MakeLavfi(root, "col.mp4", "color=c=0xC05050:s=320x180:r=15", seconds: 1);
        var s0 = RunOk1("vid-adjust", Out(root), new[] { sat }, ("saturation", "-100"));
        var m = FrameAt(s0, 3).Mean(0, 0, 320, 180);
        Assert.True(Math.Abs(m.R - m.G) < 3 && Math.Abs(m.G - m.B) < 3);
        var s1 = RunOk1("vid-adjust", Out(root), new[] { sat }, ("saturation", "100"));
        var m1 = FrameAt(s1, 3).Mean(0, 0, 320, 180);
        var mo = FrameAt(sat, 3).Mean(0, 0, 320, 180);
        Assert.True(m1.R - m1.G > mo.R - mo.G + 10);
    }

    [Fact]
    public void Filters_all_five_at_strength_extremes_on_webm_and_avi()
    {
        var root = NewDir();
        var webm = MakeVideo(root, "w.webm", seconds: 1, audio: true, size: "322x182");
        var avi = MakeVideo(root, "a.avi", seconds: 1, audio: true);
        foreach (var f in new[] { webm, avi })
            foreach (var name in new[] { "grayscale", "sepia", "blur", "sharpen", "vignette" })
                foreach (var s in new[] { "1", "100" })
                {
                    var o = RunOk1("vid-filters", Out(root), new[] { f }, ("filter", name), ("strength", s));
                    var p = Probe(o);
                    Assert.NotNull(p.AudioCodec);
                    Assert.Equal(f == avi ? 320 : 322, p.Width);
                }
    }

    [Fact]
    public void Filters_blur_sharpen_vignette_do_what_they_say()
    {
        var root = NewDir();
        var clip = MakeVideo(root, "t.mp4", seconds: 1, audio: false);
        var b = RunOk1("vid-filters", Out(root), new[] { clip }, ("filter", "blur"), ("strength", "80"));
        var sh = RunOk1("vid-filters", Out(root), new[] { clip }, ("filter", "sharpen"), ("strength", "80"));
        var s0 = FrameAt(clip, 3).Sharpness();
        Assert.True(FrameAt(b, 3).Sharpness() < s0 * 0.7);
        Assert.True(FrameAt(sh, 3).Sharpness() > s0 * 1.05);
        var flat = MakeFlat(root, "f.mp4", seconds: 1, audio: false, color: "white");
        var vv = RunOk1("vid-filters", Out(root), new[] { flat }, ("filter", "vignette"), ("strength", "80"));
        var fr = FrameAt(vv, 3);
        Assert.True(fr.MeanLuma(0, 0, 15, 15) < fr.MeanLuma(150, 80, 20, 20) - 30);
    }

    [Fact]
    public void Filters_sepia_has_warm_cast()
    {
        var root = NewDir();
        var clip = MakeFlat(root, "f.mp4", seconds: 1, audio: false, color: "0x808080");
        var s = RunOk1("vid-filters", Out(root), new[] { clip }, ("filter", "sepia"), ("strength", "100"));
        var m = FrameAt(s, 3).Mean(0, 0, 320, 180);
        Assert.True(m.R > m.B + 25, m.ToString());
    }

    [Fact]
    public void RemoveLogo_edges_window_and_rejects_outside()
    {
        var root = NewDir();
        var clip = MakeLavfi(root, "l.mp4", "color=c=0x606060:s=320x180:r=15,drawbox=x=100:y=60:w=60:h=30:color=white:t=fill", seconds: 3);
        var o = RunOk1("vid-remove-logo", Out(root), new[] { clip }, ("x", "1"), ("y", "1"), ("width", "318"), ("height", "178"));
        Assert.True(FrameAt(o, 3).MeanLuma(105, 65, 50, 20) < 200);
        var w = RunOk1("vid-remove-logo", Out(root), new[] { clip }, ("x", "96"), ("y", "56"), ("width", "68"), ("height", "38"), ("start", "1"), ("end", "2"));
        Assert.True(FrameAt(w, 3).MeanLuma(105, 65, 50, 20) > 240);
        Assert.True(FrameAt(w, 22).MeanLuma(105, 65, 50, 20) < 140);
        Assert.True(FrameAt(w, 40).MeanLuma(105, 65, 50, 20) > 240);
        Assert.False(Run("vid-remove-logo", Out(root), new[] { clip }, ("x", "300"), ("y", "10"), ("width", "30"), ("height", "10")).Ok);
        Assert.False(Run("vid-remove-logo", Out(root), new[] { clip }, ("x", "0"), ("y", "10"), ("width", "30"), ("height", "10")).Ok);
    }

    [Fact]
    public void RemoveLogo_odd_size_and_odd_rect()
    {
        var root = NewDir();
        var clip = MakeLavfi(root, "l.mp4", "color=c=0x606060:s=321x181:r=15,drawbox=x=101:y=61:w=59:h=29:color=white:t=fill", seconds: 1);
        var o = RunOk1("vid-remove-logo", Out(root), new[] { clip }, ("x", "99"), ("y", "59"), ("width", "63"), ("height", "33"));
        Assert.True(FrameAt(o, 3).MeanLuma(105, 65, 50, 20) < 140);
    }

    [Fact]
    public void Slideshow_same_names_odd_sizes_formats_duration_and_framecount()
    {
        var root = NewDir();
        var a = MakeImage(Dir(root, "a"), "p.png", "color=c=red:s=101x57");
        var b = MakeImage(Dir(root, "b"), "p.png", "color=c=blue:s=57x101");
        var c = MakeImage(Dir(root, "c"), "p.jpg", "testsrc=s=200x100");
        var d = MakeImage(Dir(root, "d"), "p.bmp", "color=c=green:s=64x64");
        var o = RunOk1("vid-slideshow", Out(root), new[] { a, b, c, d }, ("seconds", "1"), ("width", "160"), ("height", "90"));
        var p = Probe(o);
        Assert.InRange(p.Duration, 3.95, 4.05);
        Assert.Equal(100, CountFrames(o));
        Assert.Equal(160, p.Width);
        Assert.True(FrameAt(o, 10).Mean(70, 35, 20, 20).R > 180);
        Assert.True(FrameAt(o, 35).Mean(70, 35, 20, 20).B > 180);
        Assert.True(FrameAt(o, 85).Mean(70, 35, 20, 20).G > 100);
        var o2 = RunOk1("vid-slideshow", Out(root), new[] { a, b }, ("seconds", "2"), ("fit", "crop"));
        Assert.NotEqual(o, o2);
    }

    [Fact]
    public void Slideshow_music_shorter_and_longer_and_no_music_means_no_audio()
    {
        var root = NewDir();
        var a = MakeImage(root, "a.png", "color=c=red:s=64x64");
        var b = MakeImage(root, "b.png", "color=c=blue:s=64x64");
        var shortM = MakeAudio(root, "s.mp3", 1);
        var longM = MakeAudio(root, "l.mp3", 30);
        var o1 = RunOk1("vid-slideshow", Out(root), new[] { a, b, shortM }, ("seconds", "2"), ("width", "64"), ("height", "64"));
        var o2 = RunOk1("vid-slideshow", Out(root), new[] { longM, a, b }, ("seconds", "2"), ("width", "64"), ("height", "64"));
        var o3 = RunOk1("vid-slideshow", Out(root), new[] { a }, ("seconds", "1"), ("width", "64"), ("height", "64"));
        Assert.InRange(Probe(o1).Duration, 3.9, 4.15);
        Assert.InRange(Probe(o2).Duration, 3.9, 4.15);
        Assert.NotNull(Probe(o1).AudioCodec);
        Assert.Null(Probe(o3).AudioCodec);
        Assert.InRange(Probe(o3).Duration, 0.95, 1.05);
        Assert.True(MeanVolumeDb(o1) > -60);
    }

    [Fact]
    public void Slideshow_rejects_corrupt_image_and_unsupported_inputs()
    {
        var root = NewDir();
        var good = MakeImage(root, "g.png", "color=c=red:s=64x64");
        var bad = IoPath.Combine(root, "bad.png"); IoFile.WriteAllBytes(bad, new byte[100]);
        var zero = IoPath.Combine(root, "zero.jpg"); IoFile.WriteAllBytes(zero, Array.Empty<byte>());
        Assert.False(Run("vid-slideshow", Out(root), new[] { good, bad }).Ok);
        Assert.False(Run("vid-slideshow", Out(root), new[] { good, zero }).Ok);
        var trunc = IoPath.Combine(root, "trunc.png");
        IoFile.WriteAllBytes(trunc, IoFile.ReadAllBytes(good).Take(40).ToArray());
        var rt = Run("vid-slideshow", Out(root), new[] { good, trunc });
        Assert.False(rt.Ok, "truncated picture produced a video: " + string.Join(",", rt.Outputs));
        var clip = MakeFlat(root, "c.mp4", seconds: 1, audio: false);
        Assert.False(Run("vid-add-image", Out(root), new[] { clip, bad }).Ok);
        Assert.False(Run("vid-add-image", Out(root), new[] { clip, trunc }).Ok);
        Assert.False(Run("vid-slideshow", Out(root), new[] { good, clip }).Ok);
        Assert.False(Run("vid-slideshow", Out(root), new[] { good }, ("width", "161")).Ok);
        Assert.Empty(Files(Out(root)));
    }

    [Fact]
    public void Gif_editor_duration_at_30fps_speed_and_size()
    {
        var root = NewDir();
        var clip = MakeVideo(root, "v.mp4", seconds: 4, audio: true, size: "320x180", rate: 30);
        var o = RunOk1("vid-gif-editor", Out(root), new[] { clip }, ("width", "101"), ("fps", "30"));
        var p = Probe(o);
        Assert.Equal("gif", p.VideoCodec);
        Assert.Equal(101, p.Width);
        Assert.InRange(p.Duration, 3.8, 4.2);
        var o2 = RunOk1("vid-gif-editor", Out(root), new[] { clip }, ("speed", "400"), ("fps", "10"));
        Assert.InRange(Probe(o2).Duration, 0.9, 1.1);
        var o3 = RunOk1("vid-gif-editor", Out(root), new[] { clip }, ("speed", "25"), ("fps", "10"), ("duration", "1"));
        Assert.InRange(Probe(o3).Duration, 3.8, 4.2);
        var o4 = RunOk1("vid-gif-editor", Out(root), new[] { clip }, ("start", "1"), ("duration", "1.5"), ("fps", "10"));
        Assert.InRange(Probe(o4).Duration, 1.4, 1.6);
        Assert.False(Run("vid-gif-editor", Out(root), new[] { clip }, ("start", "9")).Ok);
        Assert.False(Run("vid-gif-editor", Out(root), new[] { clip }, ("width", "8")).Ok);
    }

    [Fact]
    public void Gif_editor_on_a_gif_speeds_up()
    {
        var root = NewDir();
        var clip = MakeVideo(root, "v.mp4", seconds: 3, audio: false, size: "160x90", rate: 30);
        var g1 = RunOk1("vid-gif-editor", Out(root), new[] { clip }, ("fps", "20"));
        var g2 = RunOk1("vid-gif-editor", IoPath.Combine(root, "second"), new[] { g1 }, ("speed", "200"));
        Assert.InRange(Probe(g2).Duration, 1.3, 1.7);
    }

    [Fact]
    public void Stabilize_odd_size_name_and_cancel()
    {
        var root = NewDir();
        var d = Dir(root, "it's");
        var clip = MakeLavfi(d, "sh'aky.mp4", "testsrc=size=320x180:rate=15,crop=w=300:h=160:x='10+8*sin(n)':y='10+8*cos(n*1.3)'", seconds: 2);
        var o = RunOk1("vid-stabilize", Out(root), new[] { clip });
        Assert.InRange(Probe(o).Duration, 1.8, 2.2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tool = Registry.Find("vid-stabilize")!;
        var o2 = Out(root) + "2";
        var r = ToolRunner.RunAsync(tool, new ToolRequest(new[] { clip }, o2, new Dictionary<string, string>()), null, cts.Token).GetAwaiter().GetResult();
        Assert.False(r.Ok);
        Assert.Empty(Files(o2));
    }

    [Fact]
    public void Cancel_mid_run_removes_partial_output_for_each_encoding_tool()
    {
        var root = NewDir();
        var clip = MakeVideo(root, "long.mp4", seconds: 60, audio: true, size: "640x360", rate: 25);
        foreach (var (id, key, val) in new[] { ("vid-adjust", "brightness", "10"), ("vid-filters", "filter", "blur"), ("vid-add-text", "text", "x") })
        {
            var tool = Registry.Find(id)!;
            using var cts = new CancellationTokenSource();
            var o = IoPath.Combine(root, "o-" + id);
            var task = ToolRunner.RunAsync(tool, new ToolRequest(new[] { clip }, o, new Dictionary<string, string> { [key] = val }), null, cts.Token);
            Thread.Sleep(700);
            cts.Cancel();
            var r = task.GetAwaiter().GetResult();
            if (r.Ok) continue;
            Assert.Empty(Files(o));
        }
    }
}
