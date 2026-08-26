using NetieSpace.Services;
using NetieSpace.Services.Timeline;

namespace NetieSpace.Tests;

/// <summary>
/// Space's real export graphs (not a parallel python copy).
/// Does not check the PARKING_LOT human Export gate.
/// </summary>
public class FfmpegExportGraphTests
{
    [Fact]
    public void Xfade_offset_is_left_duration_minus_wipe()
    {
        Assert.Equal(1.1, FfmpegExportGraph.XfadeOffsetSeconds(
            TimeSpan.FromSeconds(1.6), TimeSpan.FromMilliseconds(500)), 3);
        var graph = FfmpegExportGraph.XfadeWithAudio(
            TimeSpan.FromSeconds(1.6), TimeSpan.FromMilliseconds(500), 320, 240);
        Assert.Contains("xfade=transition=wipeleft:duration=0.5:offset=1.1", graph);
        Assert.Contains("acrossfade=d=0.5", graph);
        Assert.Contains("scale=320:240", graph);
        Assert.Equal("wipeleft", TimelineProject.XfadeWipeName);
    }

    [Fact]
    public void Opacity_fade_skipped_on_wipe_edge()
    {
        var clip = new TimelineClip
        {
            SourceIn = TimeSpan.Zero,
            SourceOut = TimeSpan.FromSeconds(2),
            FadeIn = TimeSpan.FromMilliseconds(200),
            FadeOut = TimeSpan.FromMilliseconds(200),
            TransitionOut = TransitionKind.Wipe
        };
        var vf = FfmpegExportGraph.ClipVideoFilters(clip, normalizeSize: false, 0, 0);
        Assert.Contains("fade=t=in:st=0:d=0.2", vf);
        Assert.DoesNotContain("fade=t=out", vf);
    }

    [Fact]
    public void Opacity_fade_out_starts_before_clip_end()
    {
        var clip = new TimelineClip
        {
            SourceIn = TimeSpan.Zero,
            SourceOut = TimeSpan.FromSeconds(2),
            FadeOut = TimeSpan.FromMilliseconds(200),
            TransitionOut = TransitionKind.Fade
        };
        var vf = FfmpegExportGraph.ClipVideoFilters(clip, normalizeSize: false, 0, 0);
        Assert.Contains("fade=t=out:st=1.8:d=0.2", vf);
    }
}
