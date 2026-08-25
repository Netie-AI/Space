using NetieSpace.Services;
using NetieSpace.Services.Timeline;

namespace NetieSpace.Tests;

/// <summary>
/// Automates Select→Split→Drag→Delete→Fade/Wipe packing in CI.
/// Does not check the PARKING_LOT human Export gate.
/// </summary>
public class VideoNleSessionTests
{
    private static VideoEditSession TenSecond(string path = @"C:\clips\a.mp4")
    {
        var session = new VideoEditSession();
        session.Reset(TimeSpan.FromSeconds(10), path);
        return session;
    }

    [Fact]
    public void Select_split_drag_delete_ripples()
    {
        var session = TenSecond();
        Assert.Equal(1, session.Clips.Count);

        session.SelectAt(TimeSpan.FromSeconds(4));
        Assert.True(session.HasSelection);
        Assert.True(session.SplitAt(TimeSpan.FromSeconds(4)));
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(TimeSpan.FromSeconds(4), session.Clips[0].End);
        Assert.Equal(TimeSpan.FromSeconds(4), session.Clips[1].Start);

        Assert.True(session.MoveClip(0, 1));
        Assert.Equal(TimeSpan.FromSeconds(4), session.Clips[0].Start);
        Assert.Equal(TimeSpan.Zero, session.Clips[1].Start);

        session.Select(0);
        Assert.True(session.DeleteSelectedOrAt(TimeSpan.Zero));
        Assert.Single(session.Clips);
        Assert.Equal(TimeSpan.Zero, session.Clips[0].Start);
        Assert.False(session.HasSelection);
    }

    [Fact]
    public void Append_clip_then_apply_order_is_permutation()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        Assert.Equal(1, session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(3)));
        Assert.True(session.HasMultipleSources);
        Assert.True(session.ApplyOrder([1, 0]));
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[0].SourcePath, ignoreCase: true);
        Assert.Equal(@"C:\clips\a.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.False(session.ApplyOrder([0, 0]));
        Assert.False(session.ApplyOrder([0]));
    }

    [Fact]
    public void Packed_split_then_wipe_join_has_duration()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(5));
        Assert.True(session.SplitAtPacked(TimeSpan.FromSeconds(4)));

        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        Assert.True(project.HasMultipleVideoSources);

        var v = project.VideoTrack!;
        Assert.True(v.Clips.Count >= 2);
        v.Clips[0].TransitionOut = TransitionKind.Wipe;
        v.Clips[0].FadeOut = TimeSpan.FromMilliseconds(500);
        v.Clips[1].TransitionIn = TransitionKind.Wipe;
        var join = TimelineProject.XfadeJoinDuration(v.Clips[0], v.Clips[1]);
        Assert.True(join > TimeSpan.Zero);
        Assert.Equal("wipeleft", TimelineProject.XfadeWipeName);

        v.Clips[0].TransitionOut = TransitionKind.Fade;
        v.Clips[1].TransitionIn = TransitionKind.Fade;
        Assert.Equal(TimeSpan.Zero, TimelineProject.XfadeJoinDuration(v.Clips[0], v.Clips[1]));
    }

    [Fact]
    public void Fade_chips_survive_kept_range_sync()
    {
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(8));
        Assert.True(project.SplitAt(TimeSpan.FromSeconds(4)));
        var left = project.VideoTrack!.Clips[0];
        left.FadeIn = TimeSpan.FromMilliseconds(200);
        left.FadeOut = TimeSpan.FromMilliseconds(200);
        left.TransitionOut = TransitionKind.Fade;

        project.SyncFromKeptRanges(project.ToKeptRanges(), @"C:\clips\a.mp4");
        Assert.Equal(TimeSpan.FromMilliseconds(200), project.VideoTrack!.Clips[0].FadeIn);
        Assert.Equal(TransitionKind.Fade, project.VideoTrack.Clips[0].TransitionOut);
    }

    [Fact]
    public void Import_clip_packs_without_overlap()
    {
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(2));
        var extra = project.ImportClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.NotNull(extra);
        Assert.Equal(TimeSpan.FromSeconds(2), extra!.TimelineStart);
        Assert.True(project.HasMultipleVideoSources);
        var v = project.VideoTrack!;
        Assert.Equal(v.Clips[0].TimelineEnd, v.Clips[1].TimelineStart);
    }

    [Fact]
    public void Split_too_close_to_edge_fails()
    {
        var session = TenSecond();
        Assert.False(session.SplitAt(TimeSpan.FromMilliseconds(50)));
    }
}
