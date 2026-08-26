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
        Assert.Single(session.Clips);

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
    public void Wipe_from_either_clip_survives_ApplyTo()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(1);
        Assert.Equal(0, session.WipeJoinIndex());
        Assert.True(session.ApplyWipeAtJoin());
        Assert.Equal(TransitionKind.Wipe, session.Clips[0].TransitionOut);
        Assert.Equal(TransitionKind.Wipe, session.Clips[1].TransitionIn);

        session.Select(0);
        Assert.Equal(0, session.WipeJoinIndex());
        Assert.True(session.ApplyFadeAt(0, TimeSpan.FromSeconds(0.5), null));
        Assert.True(session.ApplyFadeAt(1, null, TimeSpan.FromSeconds(0.5)));

        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        var left = project.VideoTrack!.Clips[0];
        var right = project.VideoTrack.Clips[1];
        Assert.Equal(TransitionKind.Wipe, left.TransitionOut);
        Assert.Equal(TransitionKind.Wipe, right.TransitionIn);
        Assert.Equal(TimeSpan.FromSeconds(0.5), left.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(0.5), right.FadeOut);
        Assert.True(TimelineProject.XfadeJoinDuration(left, right) > TimeSpan.Zero);
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
    public void SourceToPacked_uses_the_clip_file_not_only_primary()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(1), session.SourceToPacked(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(11),
            session.SourceToPacked(TimeSpan.FromSeconds(1), @"C:\clips\b.mp4"));
        Assert.Equal(TimeSpan.FromSeconds(1),
            session.SourceToPacked(TimeSpan.FromSeconds(1), @"C:\clips\a.mp4"));
    }

    [Fact]
    public void MarkToPacked_uses_selected_clip_file()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(1);
        Assert.Equal(TimeSpan.FromSeconds(11),
            session.MarkToPacked(TimeSpan.FromSeconds(1)));
        session.Select(0);
        Assert.Equal(TimeSpan.FromSeconds(1),
            session.MarkToPacked(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void PackedStartOf_second_clip_is_not_source_in()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, session.PackedStartOf(0));
        Assert.Equal(TimeSpan.FromSeconds(10), session.PackedStartOf(1));
        Assert.Equal(TimeSpan.Zero, session.Clips[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(10), session.PackedEndOf(0));
        Assert.Equal(TimeSpan.FromSeconds(12), session.PackedEndOf(1));
    }

    [Fact]
    public void SlicePacked_second_clip_keeps_that_file()
    {
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        Assert.NotNull(project.ImportClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2)));
        Assert.True(project.SlicePacked(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12)));
        var v = project.VideoTrack!;
        Assert.Single(v.Clips);
        Assert.Equal(@"C:\clips\b.mp4", v.Clips[0].SourcePath);
        Assert.Equal(TimeSpan.Zero, v.Clips[0].TimelineStart);
        Assert.Equal(TimeSpan.Zero, v.Clips[0].SourceIn);
        Assert.Equal(TimeSpan.FromSeconds(2), v.Clips[0].SourceOut);
    }

    [Fact]
    public void SlicePacked_across_join_keeps_both_files()
    {
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        Assert.NotNull(project.ImportClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2)));
        Assert.True(project.SlicePacked(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(11)));
        var v = project.VideoTrack!;
        Assert.Equal(2, v.Clips.Count);
        Assert.Equal(@"C:\clips\a.mp4", v.Clips[0].SourcePath);
        Assert.Equal(@"C:\clips\b.mp4", v.Clips[1].SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(9), v.Clips[0].SourceIn);
        Assert.Equal(TimeSpan.FromSeconds(10), v.Clips[0].SourceOut);
        Assert.Equal(TimeSpan.Zero, v.Clips[1].SourceIn);
        Assert.Equal(TimeSpan.FromSeconds(1), v.Clips[1].SourceOut);
        Assert.Equal(v.Clips[0].TimelineEnd, v.Clips[1].TimelineStart);
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

    [Fact]
    public void Clone_is_independent_of_later_reset()
    {
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(2));
        var extra = project.ImportClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.NotNull(extra);
        project.VideoTrack!.Clips[0].TransitionOut = TransitionKind.Wipe;
        project.VideoTrack.Clips[1].TransitionIn = TransitionKind.Wipe;
        project.ExportPreset = ExportPreset.Hd1080;
        var snap = project.Clone();

        project.ResetFromSource(@"C:\other.mp4", TimeSpan.FromSeconds(9));
        Assert.Equal(2, snap.VideoTrack!.Clips.Count);
        Assert.Equal(TransitionKind.Wipe, snap.VideoTrack.Clips[0].TransitionOut);
        Assert.Equal(TransitionKind.Wipe, snap.VideoTrack.Clips[1].TransitionIn);
        Assert.Equal(@"C:\clips\a.mp4", snap.PrimarySourcePath, ignoreCase: true);
        Assert.Equal(ExportPreset.Hd1080, snap.ExportPreset);
        Assert.Single(project.VideoTrack!.Clips);
    }

    [Fact]
    public void Failed_split_undo_does_not_consume_plus_clip()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.PushUndo();
        Assert.False(session.SplitAtPacked(TimeSpan.Zero));
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.True(session.TryUndo());
        Assert.Single(session.Clips);
    }

    [Fact]
    public void Unchanged_fade_undo_does_not_consume_plus_clip()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.PushUndo();
        Assert.False(session.ApplyFadeAt(0, TimeSpan.Zero, null));
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.True(session.TryUndo());
        Assert.Single(session.Clips);
    }

    [Fact]
    public void Second_wipe_undo_does_not_consume_plus_clip()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(0);
        session.PushUndo();
        Assert.True(session.ApplyWipeAtJoin());
        session.PushUndo();
        Assert.False(session.ApplyWipeAtJoin());
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(TransitionKind.Wipe, session.Clips[0].TransitionOut);
        Assert.True(session.TryUndo());
        Assert.Equal(TransitionKind.None, session.Clips[0].TransitionOut);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.True(session.TryUndo());
        Assert.Single(session.Clips);
    }

    [Fact]
    public void Undo_after_fade_and_wipe_keeps_plus_clip()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(0);
        session.PushUndo();
        Assert.True(session.ApplyFadeAt(0, TimeSpan.FromSeconds(0.5), null));
        session.PushUndo();
        Assert.True(session.ApplyWipeAtJoin());
        Assert.Equal(TransitionKind.Wipe, session.Clips[0].TransitionOut);
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(TransitionKind.None, session.Clips[0].TransitionOut);
        Assert.Equal(TimeSpan.FromSeconds(0.5), session.Clips[0].FadeIn);
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(TimeSpan.Zero, session.Clips[0].FadeIn);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
    }

    [Fact]
    public void Wipe_then_split_keeps_one_join()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(0);
        Assert.True(session.ApplyWipeAtJoin());
        Assert.True(session.SplitAtPacked(TimeSpan.FromSeconds(4)));
        Assert.Equal(3, session.Clips.Count);
        Assert.Equal(TransitionKind.None, session.Clips[0].TransitionOut);
        Assert.Equal(TransitionKind.Wipe, session.Clips[1].TransitionOut);
        Assert.Equal(TransitionKind.None, session.Clips[1].TransitionIn);
        Assert.Equal(TransitionKind.Wipe, session.Clips[2].TransitionIn);
    }

    [Fact]
    public void Leave_black_undo_removes_overlay_card()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");

        session.Select(1);
        session.PushUndo(project);
        session.FillGapsWithBlack = true;
        project.FillGapsWithBlack = true;
        Assert.True(session.DeleteSelectedOrAt(TimeSpan.Zero));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        project.AddOverlay(OverlayKind.Black, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        Assert.Single(project.OverlayTrack!.Clips);
        Assert.Equal(OverlayKind.Black, project.OverlayTrack.Clips[0].Overlay);

        Assert.True(session.TryUndo(project));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.Empty(project.OverlayTrack!.Clips);
        Assert.False(session.FillGapsWithBlack);
        Assert.False(project.FillGapsWithBlack);
        Assert.True(session.TryUndo());
        Assert.Single(session.Clips);
    }

    [Fact]
    public void Leave_black_last_clip_keeps_ruler_for_overlay()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(12), session.PackedDuration(false));

        session.Select(1);
        session.FillGapsWithBlack = true;
        var gapStart = session.PackedStartOf(1);
        var gapDur = session.PackedEndOf(1) - gapStart;
        Assert.Equal(TimeSpan.FromSeconds(10), gapStart);
        Assert.Equal(TimeSpan.FromSeconds(2), gapDur);

        Assert.True(session.DeleteSelectedOrAt(TimeSpan.Zero));
        // Ripple drops clip 2. FillGaps only inserts same-source holes.
        Assert.Equal(TimeSpan.FromSeconds(10), session.PackedDuration(true));

        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        project.AddOverlay(OverlayKind.Black, gapStart, gapDur);

        Assert.Equal(TimeSpan.FromSeconds(12),
            session.RulerDuration(project.OverlayTrack?.Clips));
        Assert.Equal(TimeSpan.FromSeconds(10),
            VideoEditSession.RulerDuration(TimeSpan.FromSeconds(10), null));
        Assert.Equal(TimeSpan.FromSeconds(10),
            VideoEditSession.RulerDuration(TimeSpan.FromSeconds(10), []));
        var inside = new TimelineClip
        {
            SourceOut = TimeSpan.FromSeconds(1),
            TimelineStart = TimeSpan.FromSeconds(1)
        };
        Assert.Equal(TimeSpan.FromSeconds(10),
            VideoEditSession.RulerDuration(TimeSpan.FromSeconds(10), [inside]));
        Assert.Equal(TimeSpan.FromSeconds(10), project.PackedVideoDuration());
        Assert.Equal(TimeSpan.FromSeconds(12), project.OverlayEnd());
        Assert.Equal(TimeSpan.FromSeconds(12), project.ExportDuration());
        Assert.Equal(TimeSpan.FromSeconds(2), project.BlackPadAfterVideo());
    }

    [Fact]
    public void Speed_survives_ApplyTo()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.Select(0);
        Assert.True(session.ApplySpeedAt(0, 2));
        Assert.False(session.ApplySpeedAt(0, 2));

        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        Assert.Equal(2, project.VideoTrack!.Clips[0].Speed, 3);
        Assert.Equal(1, project.VideoTrack.Clips[1].Speed, 3);
        Assert.Equal(TimeSpan.FromSeconds(5), project.VideoTrack.Clips[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(5), project.VideoTrack.Clips[1].TimelineStart);
        Assert.Equal(project.VideoTrack.Clips[0].TimelineEnd, project.VideoTrack.Clips[1].TimelineStart);
    }

    [Fact]
    public void Packed_play_clock_continues_past_clip1_end()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        var packed = session.PackedDuration(false).TotalSeconds;
        Assert.Equal(12, packed, 3);
        Assert.True(VideoEditSession.NeedsPackedPlayClock(packed, 10));
        Assert.False(VideoEditSession.NeedsPackedPlayClock(10, 10));
        Assert.True(VideoEditSession.PackedPlayContinuesAfterSourceEnded(10, packed));
        Assert.False(VideoEditSession.PackedPlayContinuesAfterSourceEnded(packed, packed));
        Assert.Equal(11, VideoEditSession.PackedPlayheadAfterElapsed(10, 1, packed), 3);
        Assert.Equal(packed, VideoEditSession.PackedPlayheadAfterElapsed(10, 5, packed), 3);
        Assert.Equal(0, VideoEditSession.PackedPlayheadAfterElapsed(0, 0, 0), 3);
    }

    [Fact]
    public void Speed_2x_shrinks_packed_duration()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(12), session.PackedDuration(false));
        session.Select(0);
        Assert.True(session.ApplySpeedAt(0, 2));
        // Accidental 2x used to keep 12s packed width so Export ~2.3s
        // looked like a full timeline until ffprobe.
        Assert.Equal(TimeSpan.FromSeconds(7), session.PackedDuration(false));
        Assert.Equal(TimeSpan.FromSeconds(5), session.PackedStartOf(1));
        Assert.Equal(TimeSpan.FromSeconds(5),
            session.PackedEndOf(0) - session.PackedStartOf(0));
        Assert.Equal(TimeSpan.FromSeconds(5), session.Clips[0].OutputDuration);
        Assert.Contains("2x", session.Describe(), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(2), session.SourceToPacked(TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(4), session.PackedToSource(TimeSpan.FromSeconds(2)));
        Assert.True(session.SplitAtPacked(TimeSpan.FromSeconds(2.5)));
        Assert.Equal(3, session.Clips.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), session.Clips[0].End);
    }

    [Fact]
    public void Unchanged_speed_undo_does_not_consume_plus_clip()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        session.PushUndo();
        Assert.False(session.ApplySpeedAt(0, 1));
        Assert.True(session.TryUndo());
        Assert.Equal(2, session.Clips.Count);
        Assert.Equal(@"C:\clips\b.mp4", session.Clips[1].SourcePath, ignoreCase: true);
        Assert.True(session.TryUndo());
        Assert.Single(session.Clips);
    }

    [Fact]
    public void Context_black_overlay_undo_removes_card()
    {
        var session = TenSecond(@"C:\clips\a.mp4");
        session.PushUndo();
        session.AppendClip(@"C:\clips\b.mp4", TimeSpan.FromSeconds(2));
        var project = new TimelineProject();
        project.ResetFromSource(@"C:\clips\a.mp4", TimeSpan.FromSeconds(10));
        session.ApplyTo(project, @"C:\clips\a.mp4");
        session.PushUndo(project);
        project.AddOverlay(OverlayKind.Black, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Assert.Single(project.OverlayTrack!.Clips);
        Assert.True(session.TryUndo(project));
        Assert.Equal(2, session.Clips.Count);
        Assert.Empty(project.OverlayTrack!.Clips);
    }
}
