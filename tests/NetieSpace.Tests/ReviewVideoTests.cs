using System.Globalization;
using System.Text.RegularExpressions;
using NetieSpace.Apps.Video;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Review fixes for the video tools (S50 wave 4). Each test was written red first.</summary>
public class ReviewVideoTests
{
    /// <summary>Mean level in dB of the 440 Hz band of a window of the first audio stream.</summary>
    private static double Level440(string path, double from, double length)
    {
        var (_, _, err) = Exec("ffmpeg", "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture),
            "-t", length.ToString(CultureInfo.InvariantCulture), "-i", path, "-vn",
            "-af", "bandpass=f=440:width_type=h:w=120,volumedetect", "-f", "null", "-");
        var m = Regex.Match(err, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        if (!m.Success) throw new InvalidOperationException("no mean_volume in: " + err);
        return m.Groups[1].Value == "-inf" ? -200 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void AddAudio_mix_keeps_the_original_level_while_the_added_audio_plays_and_after_it_ends()
    {
        var dir = NewDir();
        var video = MakeVideo(dir, "clip.mp4", seconds: 4); // a 440 Hz tone
        var added = MakeAudio(dir, "short.wav", seconds: 1, freq: 880); // ends a quarter of the way in

        var mixed = RunOk1("vid-add-audio", IoPath.Combine(dir, "out"), new[] { video, added }, ("mode", "mix"));

        var during = Level440(mixed, 0.2, 0.7);
        var after = Level440(mixed, 2.0, 1.8);
        var originalDuring = Level440(video, 0.2, 0.7);
        var originalAfter = Level440(video, 2.0, 1.8);
        Assert.True(Math.Abs(during - originalDuring) < 1.5, $"while the added audio plays the original is at {during:F1} dB, it was {originalDuring:F1} dB");
        Assert.True(Math.Abs(after - originalAfter) < 1.5, $"after the added audio ends the original is at {after:F1} dB, it was {originalAfter:F1} dB");
        Assert.True(Math.Abs(during - after) < 1.5, $"the original jumps from {during:F1} dB to {after:F1} dB when the added audio ends");
    }

    // Stand-in for ffmpeg's frame queue between palettegen and paletteuse: 4 bytes a pixel, every frame of the clip.
    private const long QueueBudget = 512L * 1024 * 1024;

    private static (int Width, int Height, int Frames) GifShape(IReadOnlyList<string> args, VideoInfo src, int fps, double seconds)
    {
        var graph = args[args.ToList().IndexOf("-filter_complex") + 1];
        var m = Regex.Match(graph, @"scale=(?:min\((\d+)\\,iw\)|(\d+)):-1");
        // No scale at all means the clip keeps its own size.
        var asked = !m.Success ? src.Width
            : int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
        var w = Math.Min(asked, src.Width);
        var h = (int)Math.Ceiling(w * (double)src.Height / src.Width);
        return (w, h, (int)Math.Ceiling(seconds * fps));
    }

    [Fact]
    public void ToGif_graph_downscales_so_the_queued_frames_stay_in_memory_budget()
    {
        var src = new VideoInfo(120, true, false, 1920, 1080, 30, "h264");
        var plan = VideoToolProvider.GifPlan("/in/clip.mp4", src, 0, null, 30, 1280);
        var (w, h, frames) = GifShape(plan.Args, src, 30, 120);
        Assert.True((long)w * h * frames * 4 <= QueueBudget, $"{frames} frames of {w}x{h} queue {(long)w * h * frames * 4 / (1024 * 1024)} MB");
        Assert.True(w >= 16);

        // A short clip is left alone: nothing is shrunk that fits.
        var small = new VideoInfo(4, true, false, 1920, 1080, 30, "h264");
        var smallPlan = VideoToolProvider.GifPlan("/in/clip.mp4", small, 0, null, 10, 480);
        Assert.Equal(480, GifShape(smallPlan.Args, small, 10, 4).Width);
    }

    [Fact]
    public void GifEditor_defaults_downscale_a_large_clip_so_the_queued_frames_stay_in_memory_budget()
    {
        var src = new VideoInfo(60, true, false, 3840, 2160, 30, "h264");
        // width 0 = keep the size, fps 0 = the clip's own rate, speed 100 = as recorded
        var plan = VideoToolProvider.GifEditorPlan("/in/clip.mp4", src, 0, null, 0, 0, 100);
        var (w, h, frames) = GifShape(plan.Args, src, 30, 60);
        Assert.True((long)w * h * frames * 4 <= QueueBudget, $"{frames} frames of {w}x{h} queue {(long)w * h * frames * 4 / (1024 * 1024)} MB");

        // The same clip at a quarter of the speed doubles the frames, so the picture shrinks further.
        var slow = VideoToolProvider.GifEditorPlan("/in/clip.mp4", src, 0, 30, 0, 0, 100);
        var (sw, _, _) = GifShape(slow.Args, src, 30, 30);
        Assert.True(sw >= w, "half the clip must not get a narrower picture than all of it");

        // A small clip keeps its size.
        var tiny = new VideoInfo(3, true, false, 320, 180, 15, "h264");
        var keep = VideoToolProvider.GifEditorPlan("/in/clip.mp4", tiny, 0, null, 0, 0, 100);
        Assert.Equal(320, GifShape(keep.Args, tiny, 15, 3).Width);
    }
}
