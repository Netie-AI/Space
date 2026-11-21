using System.Globalization;
using NetieSpace.Apps.Shell.Panels.Video;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>
/// W6-4: the quick edit behind the filmstrip, driven through ToolRunner like every tool, on real clips made with ffmpeg
/// and read back with ffprobe / volumedetect / decoded frames. Timing-sensitive tests cancel on a progress report.
/// </summary>
public class VideoFlowEngineTests
{
    private static ToolResult RunFlow(string outDir, string input, params (string Key, string Value)[] opts)
    {
        var tool = VideoToolProvider.FlowTool(ToolAvailability.Yes);
        var req = new ToolRequest(new[] { input }, outDir, opts.ToDictionary(o => o.Key, o => o.Value));
        return ToolRunner.RunAsync(tool, req, null, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Ok(ToolResult r)
    {
        Assert.True(r.Ok, r.Error);
        return Assert.Single(r.Outputs);
    }

    /// <summary>Mean volume of the part of a file from <paramref name="from"/> for <paramref name="len"/> seconds.</summary>
    private static double VolumeOf(string path, double from, double len)
    {
        var (_, _, err) = Exec("ffmpeg", "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture), "-t", len.ToString(CultureInfo.InvariantCulture),
            "-i", path, "-vn", "-af", "volumedetect", "-f", "null", "-");
        var m = System.Text.RegularExpressions.Regex.Match(err, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        Assert.True(m.Success, "no mean_volume in: " + err);
        return m.Groups[1].Value == "-inf" ? -200 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>A busy picture (noise) at about 6 Mbit/s: the size the WhatsApp preset has to bring down.</summary>
    private static string MakeNoisy(string dir, string name, double seconds)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        var (code, _, err) = Exec("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30,noise=alls=35:allf=t+u",
            "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100", "-t", seconds.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "6M", "-pix_fmt", "yuv420p", "-c:a", "aac", path);
        Assert.True(code == 0, err);
        return path;
    }

    [Fact]
    public void Trim_duration_is_within_a_fifth_of_a_second_of_the_handles()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 8, rate: 25);
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("in", "1.5"), ("out", "6.2")));
        Assert.Equal(4.7, Probe(o).Duration, 0.2);
        Assert.Equal("h264", Probe(o).VideoCodec);
        Assert.Contains("-edit", IoPath.GetFileName(o));
    }

    [Fact]
    public void Times_can_be_clock_text_and_the_end_defaults_to_the_end_of_the_clip()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 6, rate: 25);
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("in", "0:02.0")));
        Assert.Equal(4.0, Probe(o).Duration, 0.25);
    }

    [Fact]
    public void A_muted_section_is_silent_and_the_rest_keeps_its_sound()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 8, rate: 25);
        // Source seconds 3..5 muted; the selection starts at 1 s, so the section is output seconds 2..4.
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("in", "1"), ("out", "7"), ("mute_from", "3"), ("mute_to", "5")));
        Assert.Equal(6.0, Probe(o).Duration, 0.25);
        Assert.True(VolumeOf(o, 2.3, 1.4) < -60, "the muted section should be silent");
        Assert.True(VolumeOf(o, 0.2, 1.4) > -30, "sound before the mute section must remain");
        Assert.True(VolumeOf(o, 4.4, 1.4) > -30, "sound after the mute section must remain");
    }

    [Fact]
    public void A_speed_section_changes_only_its_own_part_of_the_length()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 8, rate: 25);
        // 8 s, seconds 2..6 at 200 percent: 2 + 2 + 2 = 6 s.
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("speed_from", "2"), ("speed_to", "6"), ("speed", "200")));
        var p = Probe(o);
        Assert.Equal(6.0, p.Duration, 0.3);
        Assert.NotNull(p.AudioCodec);
        Assert.InRange(CountFrames(o), 6 * 25 - 8, 6 * 25 + 8);
    }

    [Fact]
    public void A_speed_section_over_the_whole_selection_slows_it()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4, rate: 25);
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("speed_from", "0"), ("speed_to", "4"), ("speed", "50")));
        Assert.Equal(8.0, Probe(o).Duration, 0.4);
    }

    [Fact]
    public void A_caption_is_drawn_on_the_picture()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, rate: 25, size: "640x360");
        var plain = Ok(RunFlow(IoPath.Combine(dir, "plain"), input));
        var cap = Ok(RunFlow(IoPath.Combine(dir, "cap"), input, ("caption", "Netie flow caption that is long enough to wrap onto a second line for sure")));
        Assert.Equal(Probe(plain).Width, Probe(cap).Width);
        var diff = MeanAbsDiff(FrameGray(plain, 10), FrameGray(cap, 10));
        Assert.True(diff > 0.5, "the caption should change the picture (mean difference " + diff + ")");
    }

    [Fact]
    public void Gif_preset_makes_a_480_px_12_fps_loop_of_the_selection()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 10, rate: 25, size: "960x540");
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("in", "2"), ("out", "8"), ("preset", "gif")));
        Assert.EndsWith(".gif", o);
        var p = Probe(o);
        Assert.Equal(480, p.Width);
        Assert.Equal(6.0, p.Duration, 0.4);
        Assert.InRange(CountFrames(o), 6 * 12 - 3, 6 * 12 + 3);
        Assert.Null(p.AudioCodec);
    }

    [Fact]
    public void Reel_preset_is_1080_by_1920_for_a_landscape_clip()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, rate: 25, size: "640x360");
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("preset", "reel")));
        var p = Probe(o);
        Assert.Equal((1080, 1920), (p.Width, p.Height));
        Assert.Equal("h264", p.VideoCodec);
        Assert.NotNull(p.AudioCodec);
    }

    [Fact]
    public void YouTube_preset_does_not_upscale_a_small_clip()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, rate: 25, size: "640x360");
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("preset", "youtube")));
        Assert.Equal((640, 360), (Probe(o).Width, Probe(o).Height));
    }

    [Fact]
    public void WhatsApp_preset_brings_a_busy_clip_under_16_MB_and_close_to_the_budget()
    {
        var dir = NewDir();
        var input = MakeNoisy(dir, "busy.mp4", seconds: 45);
        var srcBytes = new System.IO.FileInfo(input).Length;
        Assert.True(srcBytes > 20_000_000, "the source must be bigger than the limit for this test to mean anything (" + srcBytes + ")");
        var o = Ok(RunFlow(IoPath.Combine(dir, "out"), input, ("preset", "whatsapp"), ("in", "2"), ("out", "42")));
        var size = new System.IO.FileInfo(o).Length;
        Assert.True(size < 16_000_000, "WhatsApp output is " + size + " bytes");
        Assert.Equal(40.0, Probe(o).Duration, 0.3);
        Assert.NotNull(Probe(o).AudioCodec);
        // The estimate shown before the run is the same arithmetic: within a third of what came out.
        var est = VideoFlowPresets.EstimateBytes("whatsapp", 40, 1280, 720, 45, srcBytes);
        Assert.InRange(size / (double)est, 0.6, 1.4);
        // No pass log or temp file is left in the output folder.
        Assert.Single(IoDirectory.GetFiles(IoPath.Combine(dir, "out")));
    }

    [Fact]
    public async Task Cancel_stops_a_two_pass_export_at_once_and_leaves_nothing()
    {
        var dir = NewDir();
        var input = MakeNoisy(dir, "busy.mp4", seconds: 30);
        var outDir = IoPath.Combine(dir, "out");
        var tool = VideoToolProvider.FlowTool(ToolAvailability.Yes);
        var req = new ToolRequest(new[] { input }, outDir, new Dictionary<string, string> { ["preset"] = "whatsapp" });
        using var cts = new CancellationTokenSource();
        var cancelAt = new CancelOnProgress(cts);
        var r = await ToolRunner.RunAsync(tool, req, cancelAt, cts.Token);
        Assert.True(cancelAt.Fired, "the export finished with no mid-run progress report; make the clip longer");
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Bad_selections_fail_with_a_message_naming_the_cause_and_leave_no_file()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4, rate: 25);
        var silent = MakeVideo(dir, "silent.mp4", seconds: 4, audio: false);
        var outDir = IoPath.Combine(dir, "out");

        var past = RunFlow(outDir, input, ("in", "9"));
        Assert.False(past.Ok);
        Assert.Contains("'in'", past.Error);

        var tiny = RunFlow(outDir, input, ("in", "2"), ("out", "2.05"));
        Assert.False(tiny.Ok);
        Assert.Contains("at least 0.1 s", tiny.Error);

        var half = RunFlow(outDir, input, ("mute_from", "1"));
        Assert.False(half.Ok);
        Assert.Contains("mute_to", half.Error);

        var mute = RunFlow(outDir, silent, ("mute_from", "1"), ("mute_to", "2"));
        Assert.False(mute.Ok);
        Assert.Contains("no sound", mute.Error);

        var preset = RunFlow(outDir, input, ("preset", "tiktok"));
        Assert.False(preset.Ok);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void The_flow_tool_is_not_a_catalog_row_so_the_tool_floor_is_untouched()
    {
        Assert.Null(Registry.Find(VideoToolProvider.FlowToolId));
        Assert.DoesNotContain(VideoToolProvider.FlowToolId, new VideoToolProvider().Runners().Select(r => r.ToolId));
    }

    // ---------------------------------------------------------------- the exe-level entry (--tool vid-flow)

    [Fact]
    public void The_cli_runs_the_flow_with_the_tool_grammar_and_writes_the_json()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 6, rate: 25);
        var outDir = IoPath.Combine(dir, "out");
        var json = IoPath.Combine(dir, "res.json");
        var args = new[] { "--tool", "vid-flow", "--in", input, "--out", outDir, "--json", json, "--opt", "in=1", "--opt", "out=4", "--opt", "preset=gif" };
        Assert.True(VideoFlowCli.Wants(args));
        Assert.False(VideoFlowCli.Wants(new[] { "--tool", "vid-trim" }));
        var o = new StringWriter();
        var e = new StringWriter();
        Assert.Equal(0, VideoFlowCli.Run(args, o, e, Registry));
        var path = o.ToString().Trim();
        Assert.EndsWith(".gif", path);
        Assert.True(IoFile.Exists(path));
        using var doc = System.Text.Json.JsonDocument.Parse(IoFile.ReadAllText(json));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("vid-flow", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public void The_cli_says_why_for_bad_arguments_and_a_failed_run()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 3, rate: 25);
        var e = new StringWriter();
        Assert.Equal(2, VideoFlowCli.Run(new[] { "--tool", "vid-flow", "--in", input }, new StringWriter(), e, Registry));
        Assert.Contains("--out", e.ToString());
        e = new StringWriter();
        Assert.Equal(2, VideoFlowCli.Run(new[] { "--tool", "vid-flow", "--in", input, "--out", dir, "--bogus", "x" }, new StringWriter(), e, Registry));
        Assert.Contains("--bogus", e.ToString());
        e = new StringWriter();
        Assert.Equal(1, VideoFlowCli.Run(new[] { "--tool", "vid-flow", "--in", input, "--out", IoPath.Combine(dir, "o"), "--opt", "in=99" }, new StringWriter(), e, Registry));
        Assert.Contains("'in'", e.ToString());
    }
}
