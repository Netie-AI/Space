using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>
/// vid-stabilize. One class (so one xunit collection) because one test flips the build-capability seam.
/// The footage is a perfectly still picture shaken by a crop window, so the only motion in it is the shake.
/// </summary>
public class VideoTool2StabilizeTests
{
    private const string Shaky =
        "testsrc=size=640x360:rate=25,trim=end_frame=1,loop=loop=-1:size=1:start=0," +
        "crop=480:270:x='80+14*sin(n*1.3)+8*sin(n*2.9)':y='45+9*sin(n*1.7)'";

    /// <summary>Mean frame-to-frame luma change over the central part of the picture, across the whole clip.</summary>
    private static double Motion(string path)
    {
        var info = Probe(path);
        var (code, bytes, err) = Exec("ffmpeg", "-v", "error", "-i", path, "-vf",
            $"crop=iw*0.6:ih*0.6,scale=96:54:flags=area,format=gray", "-f", "rawvideo", "-");
        Assert.True(code == 0, err);
        const int frame = 96 * 54;
        var n = bytes.Length / frame;
        Assert.True(n > 20, "enough frames decoded: " + n);
        double sum = 0;
        for (var f = 5; f < n; f++)
        {
            double d = 0;
            for (var i = 0; i < frame; i++) d += Math.Abs(bytes[f * frame + i] - bytes[(f - 1) * frame + i]);
            sum += d / frame;
        }
        return sum / (n - 5);
    }

    [Fact]
    public void Stabilize_removes_most_of_the_shake()
    {
        var dir = NewDir();
        var input = MakeLavfi(dir, "shaky.mp4", Shaky, seconds: 3, rate: 25);
        var before = Motion(input);
        Assert.True(before > 3, "the test footage really shakes: " + before);

        var o = RunOk1("vid-stabilize", Out(dir), new[] { input }, ("strength", "80"));

        var after = Motion(o);
        Assert.True(after < before * 0.5, $"shake {before:0.00} -> {after:0.00}");
        var info = Probe(o);
        Assert.Equal((480, 270), (info.Width, info.Height));
        Assert.InRange(info.Duration, 2.9, 3.2);
        Assert.Empty(Files(IoPath.GetTempPath()).Where(f => IoPath.GetFileName(f).StartsWith("_ns_trf_")));
    }

    [Fact]
    public void Stabilize_through_the_cli_path()
    {
        var dir = NewDir();
        var input = MakeLavfi(dir, "shaky.mp4", Shaky, seconds: 2, rate: 25);
        var o = CliOk("vid-stabilize", Out(dir), new[] { input })[0];
        Assert.True(Motion(o) < Motion(input));
    }

    [Fact]
    public void Stabilize_says_so_when_the_ffmpeg_build_has_no_stabilizer_and_falls_back_to_deshake_when_it_can()
    {
        var dir = NewDir();
        var input = MakeLavfi(dir, "shaky.mp4", Shaky, seconds: 2, rate: 25);
        try
        {
            VideoToolProvider.FilterProbeOverride = _ => false;
            var none = Run("vid-stabilize", Out(dir), new[] { input });
            Assert.False(none.Ok);
            Assert.Contains("neither vidstab nor deshake", none.Error);
            Assert.Empty(Files(Out(dir)));

            VideoToolProvider.FilterProbeOverride = name => name == "deshake";
            var o = RunOk1("vid-stabilize", Out(dir), new[] { input });
            Assert.True(Motion(o) < Motion(input), "deshake also calms the picture");
        }
        finally
        {
            VideoToolProvider.FilterProbeOverride = null;
        }
    }

    [Fact]
    public void Stabilize_names_a_bad_strength()
    {
        var dir = NewDir();
        var input = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        var r = Run("vid-stabilize", Out(dir), new[] { input }, ("strength", "0"));
        Assert.False(r.Ok);
        Assert.Contains("'strength'", r.Error);
    }

    [Fact]
    public async Task Stabilize_cancel_leaves_no_output_and_no_temp_file()
    {
        var dir = NewDir();
        var input = MakeLavfi(dir, "long.mp4", Shaky.Replace("640x360", "1280x720"), seconds: 8, rate: 25);
        var tool = Registry.Find("vid-stabilize")!;
        using var cts = new CancellationTokenSource();
        var task = ToolRunner.RunAsync(tool, new ToolRequest(new[] { input }, Out(dir), new Dictionary<string, string>()), null, cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        var r = await task;
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
        Assert.Empty(Files(Out(dir)));
        Assert.Empty(Files(IoPath.GetTempPath()).Where(f => IoPath.GetFileName(f).StartsWith("_ns_trf_")));
    }
}
