using System.Globalization;
using System.Text.Json;
using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Audit of the video runners on inputs of unusual but legal shape: rotation tags, odd pixels, extra tracks, variable rate.</summary>
public class AuditVideoTests
{
    private sealed record Stream(string Type, string Codec, int W, int H, double Duration, string AvgRate, double Sar, int Rotation);

    private static List<Stream> Streams(string path)
    {
        var (code, stdout, err) = Exec("ffprobe", "-v", "error", "-print_format", "json", "-show_streams", "-i", path);
        Assert.True(code == 0, "ffprobe: " + err);
        using var doc = JsonDocument.Parse(stdout);
        var list = new List<Stream>();
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            string Str(string k) => s.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            int Int(string k) => s.TryGetProperty(k, out var v) ? v.GetInt32() : 0;
            double Dur = double.TryParse(Str("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            var sar = 1.0;
            var sarText = Str("sample_aspect_ratio");
            if (sarText.Contains(':'))
            {
                var p = sarText.Split(':');
                sar = double.Parse(p[0], CultureInfo.InvariantCulture) / double.Parse(p[1], CultureInfo.InvariantCulture);
            }
            var rot = 0;
            if (s.TryGetProperty("side_data_list", out var sd))
                foreach (var e in sd.EnumerateArray())
                    if (e.TryGetProperty("rotation", out var r)) rot = (int)Math.Round(r.GetDouble());
            list.Add(new Stream(Str("codec_type"), Str("codec_name"), Int("width"), Int("height"), Dur, Str("avg_frame_rate"), sar, rot));
        }
        return list;
    }

    private static Stream Video(string path) => Streams(path).First(s => s.Type == "video");
    private static int AudioCount(string path) => Streams(path).Count(s => s.Type == "audio");

    private static string Ff(params string[] args)
    {
        var (code, _, err) = Exec("ffmpeg", new[] { "-y", "-v", "error" }.Concat(args).ToArray());
        Assert.True(code == 0, "ffmpeg: " + err);
        return args[^1];
    }

    /// <summary>Stored landscape 640x360 with a 90 degree display rotation: a phone clip held upright.</summary>
    private static string Portrait(string dir, double seconds = 2, bool audio = true)
    {
        var baseClip = MakeVideo(dir, "stored.mp4", seconds, audio, "640x360", 15);
        return Ff("-display_rotation:v:0", "90", "-i", baseClip, "-c", "copy", IoPath.Combine(dir, "phone.mp4"));
    }

    private static string TwoTracks(string dir, double seconds = 2)
    {
        IoDirectory.CreateDirectory(dir);
        return Ff("-f", "lavfi", "-i", "testsrc=size=320x180:rate=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=44100", "-map", "0:v", "-map", "1:a", "-map", "2:a",
            "-t", seconds.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", IoPath.Combine(dir, "two.mp4"));
    }

    /// <summary>320x240 stored pixels shown 2:1 wide, so displayed 640x240.</summary>
    private static string Anamorphic(string dir)
    {
        IoDirectory.CreateDirectory(dir);
        return Ff("-f", "lavfi", "-i", "testsrc=size=320x240:rate=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "2",
            "-vf", "setsar=2/1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", IoPath.Combine(dir, "wide.mp4"));
    }

    // ---------------- rotation tags ----------------

    [Fact]
    public void A_portrait_phone_clip_is_sized_and_cropped_as_a_viewer_sees_it()
    {
        var dir = NewDir();
        var phone = Portrait(dir);
        var src = Video(phone);
        Assert.Equal((640, 360), (src.W, src.H));
        Assert.Equal(90, Math.Abs(src.Rotation));

        // resize: width 180 of a 360x640 picture is 180x320
        var resized = RunOk1("vid-resize", NewDir(), new[] { phone }, ("width", "180"));
        Assert.Equal((180, 320), (Video(resized).W, Video(resized).H));

        // crop: 200x500 fits the upright 360x640 picture though it is taller than the stored 360 lines
        var cropped = RunOk1("vid-crop", NewDir(), new[] { phone }, ("x", "10"), ("y", "20"), ("width", "200"), ("height", "500"));
        Assert.Equal((200, 500), (Video(cropped).W, Video(cropped).H));

        // and a crop wider than the upright picture is refused with the reason, whatever the stored width says
        var o = NewDir();
        var tooWide = Run("vid-crop", o, new[] { phone }, ("width", "500"), ("height", "200"));
        Assert.False(tooWide.Ok);
        Assert.Contains("does not fit", tooWide.Error!);
        Assert.Contains("360x640", tooWide.Error!);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o));

        // merge keeps the portrait canvas instead of squeezing the clips into a landscape one
        var merged = RunOk1("vid-merge", NewDir(), new[] { phone, phone });
        Assert.Equal((360, 640), (Video(merged).W, Video(merged).H));
    }

    [Fact]
    public void Stream_copy_tools_keep_the_rotation_tag_and_other_tools_turn_the_picture_upright()
    {
        var dir = NewDir();
        var phone = Portrait(dir);
        var muted = RunOk1("vid-mute", NewDir(), new[] { phone });
        var m = Video(muted);
        Assert.Equal((640, 360), (m.W, m.H));
        Assert.Equal(90, Math.Abs(m.Rotation));

        foreach (var id in new[] { "vid-trim", "vid-flip", "vid-compress", "vid-speed", "vid-adjust", "vid-filters" })
        {
            var opts = id switch
            {
                "vid-trim" => new[] { ("start", "0.5") },
                "vid-adjust" => new[] { ("brightness", "10") },
                "vid-compress" => new[] { ("quality", "low") },
                _ => Array.Empty<(string, string)>(),
            };
            var r = Run(id, NewDir(), new[] { phone }, opts);
            if (!r.Ok && id == "vid-compress") continue; // may not get smaller on a tiny clip; that is a refusal, not a wrong file
            Assert.True(r.Ok, id + ": " + r.Error);
            var v = Video(r.Outputs[0]);
            Assert.True((v.W, v.H) == (360, 640), id + " gave " + v.W + "x" + v.H + " for an upright 360x640 clip");
            Assert.Equal(0, v.Rotation);
        }
    }

    // ---------------- every audio track ----------------

    [Fact]
    public void A_clip_with_two_audio_tracks_keeps_both_through_tools_that_are_not_about_the_sound()
    {
        var dir = NewDir();
        var two = TwoTracks(dir);
        Assert.Equal(2, AudioCount(two));
        var bad = new List<string>();
        var cases = new (string Id, (string, string)[] Opts)[]
        {
            ("vid-resize", new[] { ("width", "160") }),
            ("vid-crop", new[] { ("width", "100"), ("height", "80") }),
            ("vid-rotate", Array.Empty<(string, string)>()),
            ("vid-flip", Array.Empty<(string, string)>()),
            ("vid-trim", new[] { ("start", "0.5") }),
            ("vid-trim", new[] { ("start", "0.5"), ("end", "1"), ("mode", "delete") }),
            ("vid-loop", Array.Empty<(string, string)>()),
            ("vid-volume", new[] { ("volume", "50") }),
            ("vid-speed", Array.Empty<(string, string)>()),
            ("vid-reverse", Array.Empty<(string, string)>()),
            ("vid-convert", new[] { ("format", "mkv") }),
            ("vid-adjust", new[] { ("hue", "20") }),
            ("vid-filters", new[] { ("filter", "sepia") }),
            ("vid-add-text", new[] { ("text", "Hi") }),
        };
        foreach (var (id, opts) in cases)
        {
            var r = Run(id, NewDir(), new[] { two }, opts);
            if (!r.Ok) { bad.Add($"{id}: {r.Error}"); continue; }
            var n = AudioCount(r.Outputs[0]);
            if (n != 2) bad.Add($"{id} ({string.Join(",", opts.Select(o => o.Item1 + "=" + o.Item2))}): {n} audio track(s) left of 2");
        }
        var split = RunOk("vid-split", NewDir(), new[] { two }, ("at", "1"));
        foreach (var s in split) if (AudioCount(s) != 2) bad.Add("vid-split part " + IoPath.GetFileName(s) + " lost a track");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    // ---------------- pixel shape ----------------

    [Fact]
    public void A_clip_with_non_square_pixels_keeps_its_displayed_shape_when_resized_merged_or_made_a_gif()
    {
        var dir = NewDir();
        var wide = Anamorphic(dir);
        Assert.Equal(2.0, Video(wide).Sar, 2);
        const double shown = 640.0 / 240.0;

        var resized = Video(RunOk1("vid-resize", NewDir(), new[] { wide }, ("width", "320")));
        Assert.True(Math.Abs(resized.W * resized.Sar / resized.H - shown) < 0.1,
            $"resize gave {resized.W}x{resized.H} sar {resized.Sar}, shape {resized.W * resized.Sar / resized.H:0.00} instead of {shown:0.00}");

        var merged = Video(RunOk1("vid-merge", NewDir(), new[] { wide, wide }));
        Assert.True(Math.Abs(merged.W * merged.Sar / merged.H - shown) < 0.1,
            $"merge gave {merged.W}x{merged.H}, shape {merged.W * merged.Sar / merged.H:0.00} instead of {shown:0.00}");

        var gif = Video(RunOk1("vid-to-gif", NewDir(), new[] { wide }, ("width", "320")));
        Assert.True(Math.Abs(gif.W / (double)gif.H - shown) < 0.1, $"gif gave {gif.W}x{gif.H}, shape {gif.W / (double)gif.H:0.00} instead of {shown:0.00}");

        // a tool that keeps the size keeps the tag, so the shape is untouched
        var compressed = Run("vid-compress", NewDir(), new[] { wide }, ("quality", "low"));
        if (compressed.Ok)
        {
            var c = Video(compressed.Outputs[0]);
            Assert.True(Math.Abs(c.W * c.Sar / c.H - shown) < 0.1, $"compress gave {c.W}x{c.H} sar {c.Sar}");
        }
    }

    // ---------------- streams copied, not re-encoded ----------------

    private static string VideoMd5(string path)
    {
        var (code, stdout, err) = Exec("ffmpeg", "-v", "error", "-i", path, "-map", "0:v:0", "-c", "copy", "-f", "md5", "-");
        Assert.True(code == 0, err);
        return System.Text.Encoding.ASCII.GetString(stdout).Trim();
    }

    [Fact]
    public void Mute_and_volume_copy_the_picture_untouched()
    {
        var dir = NewDir();
        var clip = MakeVideo(dir, "clip.mp4", 2, true, "320x180", 24);
        var original = VideoMd5(clip);
        Assert.Equal(original, VideoMd5(RunOk1("vid-mute", NewDir(), new[] { clip })));
        Assert.Equal(original, VideoMd5(RunOk1("vid-volume", NewDir(), new[] { clip }, ("volume", "60"))));
        Assert.Equal(original, VideoMd5(RunOk1("vid-add-audio", NewDir(), new[] { clip, MakeAudio(dir, "m.mp3", 2) })));
    }

    // ---------------- frame rate and sync ----------------

    [Fact]
    public void Frame_rate_and_audio_sync_survive_the_tools_that_re_encode()
    {
        var dir = NewDir();
        var clip = MakeVideo(dir, "clip.mp4", 4, true, "320x180", 24);
        var bad = new List<string>();
        var cases = new (string Id, (string, string)[] Opts, double Expect)[]
        {
            ("vid-resize", new[] { ("width", "160") }, 4),
            ("vid-flip", Array.Empty<(string, string)>(), 4),
            ("vid-rotate", Array.Empty<(string, string)>(), 4),
            ("vid-crop", new[] { ("width", "100"), ("height", "60") }, 4),
            ("vid-trim", new[] { ("start", "1"), ("end", "3.5") }, 2.5),
            ("vid-trim", new[] { ("start", "1"), ("end", "2"), ("mode", "delete") }, 3),
            ("vid-convert", new[] { ("format", "mov") }, 4),
            ("vid-loop", Array.Empty<(string, string)>(), 8),
            ("vid-speed", new[] { ("speed", "200") }, 2),
            ("vid-adjust", new[] { ("contrast", "20") }, 4),
        };
        foreach (var (id, opts, expect) in cases)
        {
            var r = Run(id, NewDir(), new[] { clip }, opts);
            if (!r.Ok) { bad.Add($"{id}: {r.Error}"); continue; }
            var s = Streams(r.Outputs[0]);
            var v = s.First(x => x.Type == "video");
            var a = s.FirstOrDefault(x => x.Type == "audio");
            var label = id + " " + string.Join(",", opts.Select(o => o.Item1 + "=" + o.Item2));
            if (v.AvgRate != "24/1" && id != "vid-trim" && id != "vid-speed") bad.Add($"{label}: frame rate {v.AvgRate} instead of 24/1");
            if (id == "vid-speed" && v.AvgRate != "24/1") bad.Add($"{label}: frame rate {v.AvgRate} after a speed change");
            if (Math.Abs(v.Duration - expect) > 0.2) bad.Add($"{label}: video lasts {v.Duration:0.00} s, expected about {expect}");
            if (a is null) { bad.Add($"{label}: lost the audio"); continue; }
            if (Math.Abs(a.Duration - v.Duration) > 1.0 / 24 + 0.06) bad.Add($"{label}: audio {a.Duration:0.000} s against video {v.Duration:0.000} s");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void A_variable_frame_rate_clip_keeps_its_length_and_sound_through_trim_resize_and_convert()
    {
        var dir = NewDir();
        IoDirectory.CreateDirectory(dir);
        var vfr = Ff("-f", "lavfi", "-i", "testsrc=size=320x180:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "4",
            "-vf", "select='not(mod(n,7))+gt(mod(n,5),2)'", "-fps_mode", "vfr", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", IoPath.Combine(dir, "vfr.mp4"));
        var src = Probe(vfr);
        var bad = new List<string>();
        foreach (var (id, opts) in new (string, (string, string)[])[]
        {
            ("vid-resize", new[] { ("width", "160") }),
            ("vid-convert", new[] { ("format", "mov") }),
            ("vid-flip", Array.Empty<(string, string)>()),
        })
        {
            var r = Run(id, NewDir(), new[] { vfr }, opts);
            if (!r.Ok) { bad.Add($"{id}: {r.Error}"); continue; }
            var s = Streams(r.Outputs[0]);
            var v = s.First(x => x.Type == "video");
            var a = s.First(x => x.Type == "audio");
            if (Math.Abs(a.Duration - src.Duration) > 0.15) bad.Add($"{id}: audio {a.Duration:0.00} s, source {src.Duration:0.00} s");
            if (Math.Abs(v.Duration - src.Duration) > 0.25) bad.Add($"{id}: video {v.Duration:0.00} s, source {src.Duration:0.00} s");
        }
        var t = RunOk1("vid-trim", NewDir(), new[] { vfr }, ("start", "1"), ("end", "3"));
        var ts = Streams(t);
        if (Math.Abs(ts.First(x => x.Type == "audio").Duration - 2) > 0.15) bad.Add("vid-trim: audio is not 2 s");
        if (Math.Abs(ts.First(x => x.Type == "video").Duration - 2) > 0.25) bad.Add("vid-trim: video is not 2 s");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    // ---------------- missing audio ----------------

    [Fact]
    public void A_clip_without_sound_given_to_an_audio_tool_is_refused_by_name_and_leaves_nothing()
    {
        var dir = NewDir();
        var silent = MakeVideo(dir, "silent.mp4", 2, audio: false);
        foreach (var (id, opts) in new (string, (string, string)[])[]
        {
            ("vid-extract-audio", Array.Empty<(string, string)>()),
            ("vid-volume", new[] { ("volume", "50") }),
            ("vid-mute", Array.Empty<(string, string)>()),
            ("vid-convert", new[] { ("format", "mp3") }),
        })
        {
            var o = NewDir();
            var r = Run(id, o, new[] { silent }, opts);
            Assert.False(r.Ok, id + " should refuse a clip with no audio");
            Assert.Contains("audio", r.Error!);
            Assert.Empty(IoDirectory.GetFileSystemEntries(o));
        }
        // tools that only carry sound along still work on a silent clip
        foreach (var id in new[] { "vid-resize", "vid-flip", "vid-speed", "vid-loop" })
        {
            var opts = id == "vid-resize" ? new[] { ("width", "160") } : Array.Empty<(string, string)>();
            var r = Run(id, NewDir(), new[] { silent }, opts);
            Assert.True(r.Ok, id + ": " + r.Error);
            Assert.Equal(0, AudioCount(r.Outputs[0]));
        }
    }

    // ---------------- times ----------------

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:30", 90)]
    [InlineData("0:01:30", 90)]
    [InlineData("00:01:30.5", 90.5)]
    [InlineData("1:00:00", 3600)]
    [InlineData(".5", 0.5)]
    public void Times_in_the_forms_people_type_mean_what_they_say(string text, double seconds)
    {
        var clip = MakeVideo(NewDir(), "t.mp4", 3, true, "160x90", 10);
        // trim from 0 to the typed time on a clip long enough: the length of the result is the typed time (capped by the clip)
        var r = Run("vid-trim", NewDir(), new[] { clip }, ("start", "0"), ("end", text));
        if (seconds <= 0) { Assert.False(r.Ok); return; }
        Assert.True(r.Ok, r.Error);
        Assert.True(Math.Abs(Probe(r.Outputs[0]).Duration - Math.Min(seconds, 3)) < 0.2, text + " gave " + Probe(r.Outputs[0]).Duration);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("2", "1")]
    [InlineData("5", "9")]
    [InlineData("abc", "1")]
    [InlineData("-1", "2")]
    [InlineData("1:75", "2")]
    [InlineData("1", "1")]
    public void Impossible_times_are_refused_naming_the_option_and_leave_nothing(string start, string end)
    {
        var clip = MakeVideo(NewDir(), "t.mp4", 3, true, "160x90", 10);
        var o = NewDir();
        var r = Run("vid-trim", o, new[] { clip }, ("start", start), ("end", end));
        Assert.False(r.Ok, $"start={start} end={end} should be refused");
        Assert.Contains("option '", r.Error!);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o));
    }
}
