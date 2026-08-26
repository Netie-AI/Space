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

    [Fact]
    public void Pad_black_after_video_uses_add_not_clone()
    {
        Assert.Equal("tpad=stop_mode=add:stop_duration=2", FfmpegExportGraph.PadBlackAfterVideo(2));
        Assert.Contains("stop_mode=add", FfmpegExportGraph.PadBlackAfterVideo(0.5));
        Assert.DoesNotContain("clone", FfmpegExportGraph.PadBlackAfterVideo(2));
    }

    [Fact]
    public void Escape_filter_path_escapes_apostrophe_and_drive_colon()
    {
        Assert.Equal(
            "C\\:/Users/Mom\\'s clips/a.srt",
            FfmpegExportGraph.EscapeFilterPath(@"C:\Users\Mom's clips\a.srt"));
        Assert.Equal("/tmp/a.srt", FfmpegExportGraph.EscapeFilterPath("/tmp/a.srt"));
    }

    [Fact]
    public void Concat_demuxer_line_uses_file_name_only()
    {
        Assert.Equal(
            "file '_ns_tlseg_ab.mp4'",
            FfmpegExportGraph.ConcatDemuxerLine(@"C:\Users\Mom's clips\_ns_tlseg_ab.mp4"));
        Assert.Equal(
            "file '_ns_a.mp4'",
            FfmpegExportGraph.ConcatDemuxerLine("/tmp/Mom's clips/_ns_a.mp4"));
        Assert.DoesNotContain("Mom", FfmpegExportGraph.ConcatDemuxerLine(@"C:\Users\Mom's clips\a.mp4"));
    }
}
