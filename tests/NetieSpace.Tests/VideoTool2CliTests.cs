using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;
using static NetieSpace.Tests.VideoTool2Support;

namespace NetieSpace.Tests;

/// <summary>
/// The second-wave tools through ToolCli.RunWith (the --tool path): registry, availability, option validation,
/// runner and output check proven together, with the decoded result read back.
/// </summary>
public class VideoTool2CliTests
{
    private static readonly string[] Wave2 =
    {
        "vid-add-text", "vid-add-image", "vid-subtitles", "vid-audio-cut-merge", "vid-adjust",
        "vid-filters", "vid-slideshow", "vid-stabilize", "vid-remove-logo", "vid-gif-editor",
    };

    [Fact]
    public void Wave2_tools_are_registered_available_and_the_rest_still_say_not_built()
    {
        var registered = new VideoToolProvider2().Runners().Select(r => r.ToolId).OrderBy(x => x).ToArray();
        Assert.Equal(Wave2.OrderBy(x => x).ToArray(), registered);
        foreach (var id in Wave2)
        {
            var t = Registry.Find(id)!;
            Assert.NotNull(t.Runner);
            Assert.True(t.Availability.Available, id + ": " + t.Availability.Text);
        }
        foreach (var id in new[] { "vid-editor", "vid-stickers", "vid-meme", "vid-stop-motion", "vid-tts" })
        {
            var t = Registry.Find(id)!;
            Assert.Null(t.Runner);
            Assert.False(t.Availability.Available);
        }
        // no tool is claimed by both providers (the registry would refuse to load)
        var first = new VideoToolProvider().Runners().Select(r => r.ToolId);
        Assert.Empty(first.Intersect(registered));
    }

    [Fact]
    public void Wave2_catalog_options_are_typed_and_defaults_pass_validation()
    {
        foreach (var id in Wave2)
        {
            var entry = Registry.Find(id)!.Entry;
            foreach (var o in entry.Options.Where(o => o.Default is not null))
            {
                var req = new ToolRequest(Array.Empty<string>(), "x", new Dictionary<string, string> { [o.Key] = o.Default! });
                var bad = ToolRunner.Validate(entry with { MinFiles = 0 }, req, out _);
                Assert.True(bad is null, id + "." + o.Key + " default is rejected by its own option definition: " + bad);
            }
        }
    }

    [Fact]
    public void Cli_vid_add_text()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        var o = CliOk("vid-add-text", Out(dir), new[] { clip }, ("text", "CLI"), ("position", "middle-center"), ("size", "48"))[0];
        Assert.True(FrameAt(o, 4).Diff(FrameAt(clip, 4), 80, 50, 160, 80) > 3);
        Assert.True(FrameAt(o, 4).Diff(FrameAt(clip, 4), 0, 0, 40, 30) < 1.5);
    }

    [Fact]
    public void Cli_vid_add_image()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        var logo = MakeImage(dir, "logo.png", "color=c=red:s=64x64");
        var o = CliOk("vid-add-image", Out(dir), new[] { clip, logo }, ("position", "bottom-left"), ("scale", "20"))[0];
        var f = FrameAt(o, 4);
        Assert.True(f.Mean(14, 130, 30, 30).R > 200 && f.Mean(14, 130, 30, 30).G < 60);
        Assert.True(f.Diff(FrameAt(clip, 4), 200, 0, 120, 100) < 1.5);
    }

    [Fact]
    public void Cli_vid_subtitles()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        var srt = IoPath.Combine(dir, "s.srt");
        WriteSrt(srt, (0.5, 1.5, "Hello there"));
        var o = CliOk("vid-subtitles", Out(dir), new[] { clip, srt }, ("size", "10"))[0];
        Assert.True(FrameAt(o, 15).Diff(FrameAt(clip, 15), 20, 120, 280, 60) > 2.5, "cue visible at 1.0 s");
        Assert.True(FrameAt(o, 27).Diff(FrameAt(clip, 27), 20, 120, 280, 60) < 1.0, "gone at 1.8 s");
    }

    [Fact]
    public void Cli_vid_subtitles_with_a_vtt_file_option()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", seconds: 2, audio: false);
        var vtt = IoPath.Combine(dir, "s.vtt");
        IoFile.WriteAllText(vtt, "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nVtt line is here\n");
        var o = CliOk("vid-subtitles", Out(dir), new[] { clip }, ("subtitles", vtt), ("size", "10"))[0];
        Assert.True(FrameAt(o, 5).Diff(FrameAt(clip, 5), 20, 120, 280, 60) > 2.5);
        Assert.True(FrameAt(o, 25).Diff(FrameAt(clip, 25), 20, 120, 280, 60) < 1.0);
    }

    [Fact]
    public void Cli_vid_audio_cut_merge()
    {
        var dir = NewDir();
        var a = MakeAudio(dir, "a.wav", seconds: 2);
        var b = MakeAudio(dir, "b.wav", seconds: 3);
        var o = CliOk("vid-audio-cut-merge", Out(dir), new[] { a, b }, ("format", "wav"))[0];
        Assert.InRange(Probe(o).Duration, 4.95, 5.05);
        var cut = CliOk("vid-audio-cut-merge", Out(dir), new[] { b }, ("start", "1"), ("end", "2"), ("format", "wav"))[0];
        Assert.InRange(Probe(cut).Duration, 0.95, 1.05);
    }

    [Fact]
    public void Cli_vid_adjust()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", color: "0x808080", seconds: 1, audio: false);
        var o = CliOk("vid-adjust", Out(dir), new[] { clip }, ("brightness", "35"))[0];
        Assert.True(FrameAt(o, 3).MeanLuma(0, 0, 320, 180) > FrameAt(clip, 3).MeanLuma(0, 0, 320, 180) + 30);
    }

    [Fact]
    public void Cli_vid_filters()
    {
        var dir = NewDir();
        var clip = MakeVideo(dir, "clip.mp4", 1, audio: false);
        var o = CliOk("vid-filters", Out(dir), new[] { clip }, ("filter", "grayscale"))[0];
        var f = FrameAt(o, 3);
        double spread = 0;
        for (var y = 0; y < f.H; y += 4)
            for (var x = 0; x < f.W; x += 4) { var (r, g, b) = f.At(x, y); spread += Math.Abs(r - g) + Math.Abs(g - b); }
        Assert.True(spread / (f.W / 4 * (f.H / 4)) < 3);
    }

    [Fact]
    public void Cli_vid_slideshow()
    {
        var dir = NewDir();
        var one = MakeImage(dir, "1.png", "color=c=red:s=80x80");
        var two = MakeImage(dir, "2.png", "color=c=lime:s=80x80");
        var o = CliOk("vid-slideshow", Out(dir), new[] { one, two }, ("seconds", "1"), ("width", "96"), ("height", "96"))[0];
        Assert.InRange(Probe(o).Duration, 1.9, 2.1);
        Assert.True(FrameAt(o, 5).Mean(30, 30, 30, 30).R > 200);
        Assert.True(FrameAt(o, 35).Mean(30, 30, 30, 30).G > 200);
    }

    [Fact]
    public void Cli_vid_remove_logo()
    {
        var dir = NewDir();
        var clip = MakeLavfi(dir, "logo.mp4", "color=c=0x606060:s=320x180:r=15,drawbox=x=100:y=60:w=60:h=30:color=white:t=fill", seconds: 1);
        var o = CliOk("vid-remove-logo", Out(dir), new[] { clip }, ("x", "96"), ("y", "56"), ("width", "68"), ("height", "38"))[0];
        Assert.True(FrameAt(clip, 3).MeanLuma(105, 65, 50, 20) > 240);
        Assert.True(FrameAt(o, 3).MeanLuma(105, 65, 50, 20) < 130);
    }

    [Fact]
    public void Cli_vid_gif_editor()
    {
        var dir = NewDir();
        var gif = IoPath.Combine(dir, "g.gif");
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=160x90:rate=10", "-t", "2", gif);
        var o = CliOk("vid-gif-editor", Out(dir), new[] { gif }, ("duration", "1"), ("width", "80"))[0];
        var info = Probe(o);
        Assert.Equal("gif", info.VideoCodec);
        Assert.Equal(80, info.Width);
        Assert.InRange(info.Duration, 0.9, 1.1);
    }

    [Fact]
    public void Cli_bad_options_exit_2_with_the_option_named_and_runtime_failures_exit_1()
    {
        var dir = NewDir();
        var clip = MakeFlat(dir, "clip.mp4", seconds: 1, audio: false);
        var bad = Cli("vid-add-text", Out(dir), new[] { clip }, ("text", "x"), ("size", "2"));
        Assert.Equal(2, bad.Exit);
        Assert.Contains("size", bad.Err);
        var noText = Cli("vid-add-text", Out(dir), new[] { clip });
        Assert.Equal(1, noText.Exit);
        Assert.Contains("'text'", noText.Err);
        var unknown = Cli("vid-filters", Out(dir), new[] { clip }, ("filter", "lomo"));
        Assert.Equal(2, unknown.Exit);
        Assert.Contains("filter", unknown.Err);
        Assert.Empty(Files(Out(dir)));
    }
}
