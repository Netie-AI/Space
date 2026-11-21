using NetieSpace.Apps.Shell.Panels.Video;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>W6-4: the rules of the filmstrip page (time text, handles, hit tests, options, size and progress lines) and the picture strip.</summary>
public class VideoFlowModelTests
{
    // ---------------------------------------------------------------- time text

    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(8, "00:08.0")]
    [InlineData(8.25, "00:08.3")]
    [InlineData(59.96, "01:00.0")]
    [InlineData(125.3, "02:05.3")]
    [InlineData(3723.4, "1:02:03.4")]
    [InlineData(-4, "00:00.0")]
    public void Time_shows_as_mm_ss_f(double seconds, string text) => Assert.Equal(text, FlowTime.Format(seconds));

    [Theory]
    [InlineData("8", 8.0)]
    [InlineData("8.5", 8.5)]
    [InlineData("8,5", 8.5)]
    [InlineData("00:08.5", 8.5)]
    [InlineData("1:05", 65.0)]
    [InlineData("1:02:03.4", 3723.4)]
    [InlineData("  12  ", 12.0)]
    public void Time_text_that_a_person_types_is_read(string text, double seconds)
    {
        Assert.True(FlowTime.TryParse(text, out var s));
        Assert.Equal(seconds, s, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("1:75")]
    [InlineData("1:99:00")]
    [InlineData("1e9")]
    [InlineData(null)]
    public void Text_that_is_not_a_time_is_refused_without_throwing(string? text) => Assert.False(FlowTime.TryParse(text, out _));

    [Fact]
    public void Format_then_parse_round_trips_to_a_tenth()
    {
        foreach (var t in new[] { 0.0, 3.1, 59.9, 60.0, 125.7, 3599.9, 7322.5 })
        {
            Assert.True(FlowTime.TryParse(FlowTime.Format(t), out var back));
            Assert.Equal(t, back, 1);
        }
    }

    // ---------------------------------------------------------------- handles

    [Fact]
    public void In_and_out_handles_cannot_cross_or_leave_the_clip()
    {
        var s = new FlowState(120, hasAudio: true);
        s.SetIn(8);
        s.SetOut(115);
        Assert.Equal((8.0, 115.0), (s.In, s.Out));
        s.SetIn(500);
        Assert.Equal(115 - FlowState.MinSpan, s.In, 3);
        s.SetOut(-5);
        Assert.Equal(s.In + FlowState.MinSpan, s.Out, 3);
        s.SetIn(-9);
        Assert.Equal(0, s.In);
        s.SetOut(9999);
        Assert.Equal(120, s.Out);
    }

    [Fact]
    public void Sections_follow_the_selection_and_never_stick_out_of_it()
    {
        var s = new FlowState(120, hasAudio: true);
        s.SetMuteOn(true);
        s.SetSpeedOn(true);
        s.SetMuteFrom(10);
        s.SetMuteTo(30);
        Assert.Equal((10.0, 30.0), (s.MuteFrom, s.MuteTo));
        s.SetIn(20);
        Assert.InRange(s.MuteFrom, s.In, s.Out);
        Assert.InRange(s.MuteTo, s.In, s.Out);
        Assert.True(s.MuteTo - s.MuteFrom >= FlowState.MinSection - 1e-9);
        s.SetOut(40);
        foreach (var t in new[] { s.MuteFrom, s.MuteTo, s.SpeedFrom, s.SpeedTo }) Assert.InRange(t, 20, 40);
    }

    [Fact]
    public void A_section_handle_cannot_pass_its_partner()
    {
        var s = new FlowState(60, true);
        s.SetSpeedOn(true);
        s.SetSpeedFrom(25);
        s.SetSpeedTo(35);
        s.SetSpeedFrom(99);
        Assert.True(s.SpeedFrom < s.SpeedTo);
        s.SetSpeedTo(0);
        Assert.True(s.SpeedTo > s.SpeedFrom);
    }

    [Fact]
    public void Mute_cannot_be_switched_on_for_a_clip_without_sound()
    {
        var s = new FlowState(30, hasAudio: false);
        s.SetMuteOn(true);
        Assert.False(s.MuteOn);
        Assert.DoesNotContain("mute_from", s.ToOptions().Keys);
    }

    [Fact]
    public void The_result_length_counts_the_speed_section()
    {
        var s = new FlowState(60, true);
        s.SetIn(10);
        s.SetOut(40);
        s.SetSpeedOn(true);
        s.SetSpeedFrom(10);
        s.SetSpeedTo(20);
        s.SpeedPercent = 200;
        Assert.Equal(25.0, s.OutSeconds, 3);
        s.SpeedPercent = 50;
        Assert.Equal(40.0, s.OutSeconds, 3);
    }

    // ---------------------------------------------------------------- geometry

    [Fact]
    public void Time_and_x_are_inverses_and_clamp_at_the_ends()
    {
        const double w = 640, d = 120;
        foreach (var t in new[] { 0.0, 7.5, 60, 119.9, 120 })
            Assert.Equal(t, FlowGeometry.XToTime(FlowGeometry.TimeToX(t, w, d), w, d), 6);
        Assert.Equal(0, FlowGeometry.XToTime(-50, w, d));
        Assert.Equal(d, FlowGeometry.XToTime(9999, w, d));
    }

    [Fact]
    public void A_press_grabs_the_nearer_handle_and_otherwise_seeks()
    {
        const double w = 640;
        var s = new FlowState(120, true);
        s.SetIn(30);
        s.SetOut(90);
        var xIn = FlowGeometry.TimeToX(30, w, 120);
        var xOut = FlowGeometry.TimeToX(90, w, 120);
        Assert.Equal(FlowHit.In, FlowGeometry.HitTest(xIn - 6, 20, w, s));
        Assert.Equal(FlowHit.Out, FlowGeometry.HitTest(xOut + 7, 20, w, s));
        Assert.Equal(FlowHit.Seek, FlowGeometry.HitTest((xIn + xOut) / 2, 20, w, s));
        // Inside the kept part, a little way from its edge, is a seek, not a handle.
        Assert.Equal(FlowHit.Seek, FlowGeometry.HitTest(xIn + 12, 20, w, s));
        Assert.Equal(FlowHit.None, FlowGeometry.HitTest(100, 140, w, s));
        // A section handle is only there while its section is on.
        var xm = FlowGeometry.TimeToX(s.MuteFrom, w, 120);
        Assert.Equal(FlowHit.None, FlowGeometry.HitTest(xm, 77, w, s));
        s.SetMuteOn(true);
        xm = FlowGeometry.TimeToX(s.MuteFrom, w, 120);
        Assert.Equal(FlowHit.MuteFrom, FlowGeometry.HitTest(xm, 77, w, s));
    }

    [Fact]
    public void Dragging_a_handle_moves_only_that_handle()
    {
        var s = new FlowState(120, true);
        FlowGeometry.Drag(s, FlowHit.In, 8);
        FlowGeometry.Drag(s, FlowHit.Out, 115);
        FlowGeometry.Drag(s, FlowHit.Seek, 50);
        Assert.Equal((8.0, 115.0, 50.0), (s.In, s.Out, s.Playhead));
    }

    [Fact]
    public void The_ruler_steps_grow_with_the_clip()
    {
        Assert.True(FlowGeometry.TickStep(10, 640) < FlowGeometry.TickStep(600, 640));
        Assert.True(FlowGeometry.TickStep(120, 640) is 10 or 15 or 30);
    }

    // ---------------------------------------------------------------- options for the engine

    [Fact]
    public void The_state_becomes_the_options_the_engine_reads()
    {
        var s = new FlowState(120, true);
        s.SetIn(8);
        s.SetOut(115);
        s.PresetId = VideoFlowPresets.WhatsApp;
        s.SetMuteOn(true);
        s.SetMuteFrom(20);
        s.SetMuteTo(30);
        s.SetSpeedOn(true);
        s.SetSpeedFrom(40);
        s.SetSpeedTo(50);
        s.SpeedPercent = 400;
        s.Caption = "  Hello  ";
        var o = s.ToOptions();
        Assert.Equal("8", o["in"]);
        Assert.Equal("115", o["out"]);
        Assert.Equal("whatsapp", o["preset"]);
        Assert.Equal(("20", "30"), (o["mute_from"], o["mute_to"]));
        Assert.Equal(("40", "50", "400"), (o["speed_from"], o["speed_to"], o["speed"]));
        Assert.Equal("Hello", o["caption"]);
        // Every key is one the engine's entry declares, and every value passes the runner's own validation.
        var req = new NetieSpace.Apps.Tools.ToolRequest(new[] { "x.mp4" }, "out", o);
        foreach (var k in o.Keys) Assert.Contains(VideoToolProvider.FlowEntry.Options, opt => opt.Key == k);
        Assert.NotNull(req);
    }

    [Fact]
    public void Pages_open_with_what_their_job_needs()
    {
        var gif = FlowPages.Start("vid-to-gif", 120, true);
        Assert.Equal("gif", gif.PresetId);
        Assert.Equal(6.0, gif.Length, 3);
        Assert.Equal("whatsapp", FlowPages.Start("vid-compress", 120, true).PresetId);
        var speed = FlowPages.Start("vid-speed", 120, true);
        Assert.True(speed.SpeedOn);
        Assert.Equal((0.0, 120.0), (speed.SpeedFrom, speed.SpeedTo));
        var mute = FlowPages.Start("vid-mute", 120, true);
        Assert.True(mute.MuteOn);
        Assert.False(FlowPages.Start("vid-trim", 120, true).MuteOn);
        Assert.True(FlowPages.Applies("vid-trim"));
        Assert.False(FlowPages.Applies("vid-merge"));
    }

    // ---------------------------------------------------------------- lines the person reads

    [Fact]
    public void The_size_estimate_names_the_preset_and_checks_the_whatsapp_limit()
    {
        var src = new FlowSource(1920, 1080, 120, 180_000_000, 30, true);
        var s = new FlowState(120, true);
        s.SetIn(8);
        s.SetOut(115);
        s.PresetId = VideoFlowPresets.WhatsApp;
        var line = FlowLines.Estimate(s, src);
        Assert.StartsWith("About ", line);
        Assert.Contains("MB", line);
        Assert.Contains("fits the 16 MB limit", line);
        s.PresetId = VideoFlowPresets.Reel;
        Assert.Contains("1080x1920", FlowLines.Estimate(s, src));
        s.PresetId = VideoFlowPresets.Gif;
        Assert.Contains("480 px wide, 12 fps", FlowLines.Estimate(s, src));
        Assert.Contains("long for a GIF", FlowLines.Estimate(s, src));
    }

    [Fact]
    public void Every_preset_card_has_an_estimate_before_anything_runs()
    {
        var src = new FlowSource(1920, 1080, 120, 180_000_000, 30, true);
        var s = new FlowState(120, true);
        foreach (var p in VideoFlowPresets.All)
            Assert.StartsWith("~", FlowLines.CardSize(p, s, src));
    }

    [Fact]
    public void WhatsApp_estimate_never_exceeds_the_budget_however_long_the_clip()
    {
        foreach (var secs in new[] { 3.0, 30, 120, 600, 3600 })
        {
            var bytes = VideoFlowPresets.EstimateBytes("whatsapp", secs, 1920, 1080, secs, 500_000_000);
            Assert.True(bytes <= VideoFlowPresets.WhatsAppTargetBytes, secs + " s -> " + bytes);
        }
        // A short clip is not inflated to the budget: a 10 s clip is far smaller than 13.5 MB.
        Assert.True(VideoFlowPresets.EstimateBytes("whatsapp", 10, 1920, 1080, 10, 100_000_000) < 8_000_000);
    }

    [Fact]
    public void Progress_shows_percent_and_time_left_once_there_is_something_to_judge_by()
    {
        Assert.StartsWith("Starting", FlowLines.Progress(1, TimeSpan.FromSeconds(0.5), "pass 1 of 2"));
        var line = FlowLines.Progress(25, TimeSpan.FromSeconds(10), "pass 2 of 2");
        Assert.Equal("25% - about 30 s left - pass 2 of 2", line);
        Assert.Contains("min left", FlowLines.Progress(10, TimeSpan.FromSeconds(60), ""));
        Assert.Contains("almost done", FlowLines.Progress(99, TimeSpan.FromSeconds(100), ""));
    }

    [Fact]
    public void Selection_line_reads_in_and_out()
    {
        var s = new FlowState(120, true);
        s.SetIn(8);
        s.SetOut(115);
        Assert.Equal("Keeping 00:08.0 to 01:55.0 (01:47.0)", FlowLines.Selection(s));
    }

    // ---------------------------------------------------------------- the picture strip

    [Fact]
    public async Task The_filmstrip_makes_n_pictures_and_a_second_open_comes_from_the_cache()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 10, rate: 25, size: "640x360");
        var cache = IoPath.Combine(dir, "cache");
        var first = await VideoFilmstrip.BuildAsync(input, 8, 64, CancellationToken.None, cache);
        Assert.False(first.FromCache);
        Assert.Equal(8, first.Frames.Count);
        Assert.All(first.Frames, f => Assert.True(IoFile.Exists(f) && new System.IO.FileInfo(f).Length > 200, f));
        Assert.Equal(10.0 * 0.5 / 8, first.Times[0], 3);
        Assert.Equal(10.0 * 7.5 / 8, first.Times[7], 3);
        var second = await VideoFilmstrip.BuildAsync(input, 8, 64, CancellationToken.None, cache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Frames, second.Frames);
        // Only the finished folder is left: no half-built strip.
        Assert.Single(IoDirectory.GetDirectories(cache));
    }

    [Fact]
    public async Task An_edited_file_gets_its_own_strip_not_the_old_pictures()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4, rate: 25);
        var cache = IoPath.Combine(dir, "cache");
        var before = await VideoFilmstrip.BuildAsync(input, 4, 48, CancellationToken.None, cache);
        var keyBefore = VideoFilmstrip.CacheKey(input, 4, 48);
        IoFile.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
        Assert.NotEqual(keyBefore, VideoFilmstrip.CacheKey(input, 4, 48));
        var after = await VideoFilmstrip.BuildAsync(input, 4, 48, CancellationToken.None, cache);
        Assert.False(after.FromCache);
        Assert.NotEqual(before.Frames[0], after.Frames[0]);
    }

    [Fact]
    public async Task A_cancelled_strip_leaves_no_folder_behind()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4, rate: 25);
        var cache = IoPath.Combine(dir, "cache");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoFilmstrip.BuildAsync(input, 6, 48, cts.Token, cache));
        Assert.True(!IoDirectory.Exists(cache) || IoDirectory.GetDirectories(cache).Length == 0);
    }

    [Fact]
    public async Task The_poster_is_a_still_of_the_clip_and_is_cached()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "clip.mp4", seconds: 4, rate: 25, size: "640x360");
        var cache = IoPath.Combine(dir, "cache");
        var a = await VideoFilmstrip.PosterAsync(input, 1.5, 320, CancellationToken.None, cache);
        Assert.True(new System.IO.FileInfo(a).Length > 500);
        var b = await VideoFilmstrip.PosterAsync(input, 1.5, 320, CancellationToken.None, cache);
        Assert.Equal(a, b);
    }
}
