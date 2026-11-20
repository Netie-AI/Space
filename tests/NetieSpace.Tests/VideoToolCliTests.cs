using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>
/// One test per built tool through ToolCli.RunWith (the --tool path), so registry, availability, option
/// validation, runner and the output check are proven together on a real clip.
/// </summary>
public class VideoToolCliTests
{
    private const double Tol = 0.35;

    private static string Clip(string dir, double seconds = 2, bool audio = true) =>
        MakeVideo(dir, "clip.mp4", seconds, audio);

    private static string Out(string dir) => IoPath.Combine(dir, "out");

    [Fact]
    public void Cli_vid_trim()
    {
        var dir = NewDir();
        var o = CliOk("vid-trim", Out(dir), new[] { Clip(dir, 4) }, ("start", "0:01"), ("end", "0:03"))[0];
        Assert.InRange(Probe(o).Duration, 2.0 - Tol, 2.0 + Tol);
    }

    [Fact]
    public void Cli_vid_split()
    {
        var dir = NewDir();
        var outs = CliOk("vid-split", Out(dir), new[] { Clip(dir, 2) }, ("parts", "2"));
        Assert.Equal(2, outs.Length);
        Assert.All(outs, o => Assert.InRange(Probe(o).Duration, 1.0 - Tol, 1.0 + Tol));
    }

    [Fact]
    public void Cli_vid_merge()
    {
        var dir = NewDir();
        var a = Clip(dir, 1);
        var b = MakeVideo(dir, "b.mp4", 1);
        var o = CliOk("vid-merge", Out(dir), new[] { a, b })[0];
        Assert.InRange(Probe(o).Duration, 2.0 - Tol, 2.0 + Tol);
    }

    [Fact]
    public void Cli_vid_mute()
    {
        var dir = NewDir();
        var o = CliOk("vid-mute", Out(dir), new[] { Clip(dir) })[0];
        Assert.Null(Probe(o).AudioCodec);
        Assert.NotNull(Probe(o).VideoCodec);
    }

    [Fact]
    public void Cli_vid_extract_audio()
    {
        var dir = NewDir();
        var o = CliOk("vid-extract-audio", Out(dir), new[] { Clip(dir) }, ("format", "flac"))[0];
        Assert.Equal("flac", Probe(o).AudioCodec);
        Assert.Null(Probe(o).VideoCodec);
    }

    [Fact]
    public void Cli_vid_convert()
    {
        var dir = NewDir();
        var o = CliOk("vid-convert", Out(dir), new[] { Clip(dir) }, ("format", "webm"))[0];
        Assert.Equal("vp9", Probe(o).VideoCodec);
    }

    [Fact]
    public void Cli_vid_compress()
    {
        var dir = NewDir();
        var input = MakeVideo(dir, "big.mp4", 2, size: "640x360", crf: 4);
        var o = CliOk("vid-compress", Out(dir), new[] { input })[0];
        Assert.True(new FileInfo(o).Length < new FileInfo(input).Length);
    }

    [Fact]
    public void Cli_vid_resize()
    {
        var dir = NewDir();
        var o = CliOk("vid-resize", Out(dir), new[] { Clip(dir) }, ("width", "160"), ("height", "90"), ("mode", "stretch"))[0];
        Assert.Equal((160, 90), (Probe(o).Width, Probe(o).Height));
    }

    [Fact]
    public void Cli_vid_crop()
    {
        var dir = NewDir();
        var o = CliOk("vid-crop", Out(dir), new[] { Clip(dir) }, ("width", "100"), ("height", "60"))[0];
        Assert.Equal((100, 60), (Probe(o).Width, Probe(o).Height));
    }

    [Fact]
    public void Cli_vid_rotate()
    {
        var dir = NewDir();
        var o = CliOk("vid-rotate", Out(dir), new[] { Clip(dir) }, ("angle", "270"))[0];
        Assert.Equal((180, 320), (Probe(o).Width, Probe(o).Height));
    }

    [Fact]
    public void Cli_vid_flip()
    {
        var dir = NewDir();
        var input = Clip(dir);
        var o = CliOk("vid-flip", Out(dir), new[] { input }, ("axis", "vertical"))[0];
        Assert.True(MeanAbsDiff(FrameGray(o, 2), FrameGray(input, 2, "vflip")) < 6);
    }

    [Fact]
    public void Cli_vid_speed()
    {
        var dir = NewDir();
        var o = CliOk("vid-speed", Out(dir), new[] { Clip(dir, 4) }, ("speed", "200"))[0];
        Assert.InRange(Probe(o).Duration, 2.0 - Tol, 2.0 + Tol);
    }

    [Fact]
    public void Cli_vid_reverse()
    {
        var dir = NewDir();
        var input = Clip(dir);
        var o = CliOk("vid-reverse", Out(dir), new[] { input })[0];
        var n = CountFrames(input);
        Assert.True(MeanAbsDiff(FrameGray(o, 0), FrameGray(input, n - 1)) < MeanAbsDiff(FrameGray(o, 0), FrameGray(input, 0)));
    }

    [Fact]
    public void Cli_vid_loop()
    {
        var dir = NewDir();
        var o = CliOk("vid-loop", Out(dir), new[] { Clip(dir) }, ("count", "2"))[0];
        Assert.InRange(Probe(o).Duration, 4.0 - Tol, 4.0 + Tol);
    }

    [Fact]
    public void Cli_vid_volume()
    {
        var dir = NewDir();
        var input = Clip(dir);
        var o = CliOk("vid-volume", Out(dir), new[] { input }, ("volume", "50"))[0];
        Assert.True(MeanVolumeDb(o) < MeanVolumeDb(input) - 4);
    }

    [Fact]
    public void Cli_vid_to_gif()
    {
        var dir = NewDir();
        var o = CliOk("vid-to-gif", Out(dir), new[] { Clip(dir) }, ("fps", "5"), ("width", "128"))[0];
        Assert.Equal("gif", Probe(o).VideoCodec);
        Assert.InRange(CountFrames(o), 8, 12);
    }

    [Fact]
    public void Cli_vid_add_audio()
    {
        var dir = NewDir();
        var video = Clip(dir, 2, audio: false);
        var tone = MakeAudio(dir, "tone.wav", 3);
        var o = CliOk("vid-add-audio", Out(dir), new[] { video, tone })[0];
        Assert.NotNull(Probe(o).AudioCodec);
        Assert.InRange(Probe(o).Duration, 2.0 - Tol, 2.0 + Tol);
    }

    [Fact]
    public void Cli_reports_runtime_failure_as_exit_1_and_bad_options_as_exit_2()
    {
        var dir = NewDir();
        var silent = Clip(dir, 1, audio: false);
        var runtime = Cli("vid-mute", Out(dir), new[] { silent });
        Assert.Equal(1, runtime.Exit);
        Assert.Contains("no audio", runtime.Err);
        var bad = Cli("vid-rotate", Out(dir), new[] { silent }, ("angle", "45"));
        Assert.Equal(2, bad.Exit);
        Assert.Contains("angle", bad.Err);
        var unbuilt = Cli("vid-stickers", Out(dir), new[] { silent });
        Assert.Equal(2, unbuilt.Exit);
        Assert.Contains("not built in this version", unbuilt.Err);
    }
}
