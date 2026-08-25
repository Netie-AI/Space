namespace NetieSpace.Services.Timeline;

public enum TrackKind
{
    Video,
    Audio,
    Overlay
}

public enum OverlayKind
{
    None,
    Text,
    Black,
    White,
    Image
}

public enum TransitionKind
{
    None,
    Fade,
    Crossfade,
    Dissolve,
    /// <summary>Clip-to-clip join via ffmpeg xfade wipeleft (not a per-clip opacity fade).</summary>
    Wipe
}

public enum ExportPreset
{
    MatchSource,
    Hd1080,
    Uhd4K
}

/// <summary>One clip on a track (source media or generated overlay).</summary>
public sealed class TimelineClip
{
    public Guid Id { get; } = Guid.NewGuid();
    public string SourcePath { get; set; } = "";
    public TimeSpan SourceIn { get; set; }
    public TimeSpan SourceOut { get; set; }
    public TimeSpan TimelineStart { get; set; }
    public double Speed { get; set; } = 1.0;
    public bool Muted { get; set; }
    public double Opacity { get; set; } = 1.0;
    public TransitionKind TransitionIn { get; set; } = TransitionKind.None;
    public TransitionKind TransitionOut { get; set; } = TransitionKind.None;
    public TimeSpan FadeIn { get; set; }
    public TimeSpan FadeOut { get; set; }
    public OverlayKind Overlay { get; set; } = OverlayKind.None;
    public string? Text { get; set; }
    public string? LutPath { get; set; }

    public TimeSpan Duration =>
        Speed <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Max(0, (SourceOut - SourceIn).TotalSeconds / Speed));

    public TimeSpan TimelineEnd => TimelineStart + Duration;
}

public sealed class TimelineTrack
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public TrackKind Kind { get; set; }
    public bool Muted { get; set; }
    public List<TimelineClip> Clips { get; } = [];
}

/// <summary>Premiere-style multi-track project backing the NLE UI and FFmpeg export.</summary>
public sealed class TimelineProject
{
    public TimeSpan SourceDuration { get; private set; }
    public double FrameRate { get; set; } = 30;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? PrimarySourcePath { get; private set; }
    public List<TimelineTrack> Tracks { get; } = [];
    public Guid? SelectedClipId { get; private set; }
    public ExportPreset ExportPreset { get; set; } = ExportPreset.MatchSource;
    public string? GlobalLutPath { get; set; }
    /// <summary>Optional SRT burned into export via ffmpeg subtitles filter.</summary>
    public string? SubtitleBurnPath { get; set; }
    /// <summary>Insert black fillers for source gaps on export (false = ripple merge).</summary>
    public bool FillGapsWithBlack { get; set; } = false;

    /// <summary>Default clip-to-clip wipe duration when FadeOut/FadeIn is unset.</summary>
    public static readonly TimeSpan DefaultXfadeDuration = TimeSpan.FromMilliseconds(500);

    public TimelineTrack? VideoTrack => Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
    public IEnumerable<TimelineTrack> AudioTracks => Tracks.Where(t => t.Kind == TrackKind.Audio);
    public TimelineTrack? OverlayTrack => Tracks.FirstOrDefault(t => t.Kind == TrackKind.Overlay);

    public TimelineClip? SelectedClip =>
        SelectedClipId is null
            ? null
            : Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == SelectedClipId);

    public bool HasSelection => SelectedClip is not null;

    public void ResetFromSource(string sourcePath, TimeSpan duration, int width = 0, int height = 0)
    {
        PrimarySourcePath = sourcePath;
        SourceDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (width > 0) Width = width;
        if (height > 0) Height = height;
        Tracks.Clear();
        SelectedClipId = null;

        var v = new TimelineTrack { Name = "V1", Kind = TrackKind.Video };
        var a1 = new TimelineTrack { Name = "A1", Kind = TrackKind.Audio };
        var a2 = new TimelineTrack { Name = "A2", Kind = TrackKind.Audio };
        var ov = new TimelineTrack { Name = "OV", Kind = TrackKind.Overlay };

        if (SourceDuration > TimeSpan.Zero && !string.IsNullOrWhiteSpace(sourcePath))
        {
            v.Clips.Add(new TimelineClip
            {
                SourcePath = sourcePath,
                SourceIn = TimeSpan.Zero,
                SourceOut = SourceDuration,
                TimelineStart = TimeSpan.Zero
            });
            a1.Clips.Add(new TimelineClip
            {
                SourcePath = sourcePath,
                SourceIn = TimeSpan.Zero,
                SourceOut = SourceDuration,
                TimelineStart = TimeSpan.Zero
            });
        }

        Tracks.Add(v);
        Tracks.Add(a1);
        Tracks.Add(a2);
        Tracks.Add(ov);
    }

    public void SelectClip(Guid? id) => SelectedClipId = id;

    public void ClearSelection() => SelectedClipId = null;

    public TimelineClip? ClipAt(TimeSpan playhead, TrackKind kind = TrackKind.Video)
    {
        var track = Tracks.FirstOrDefault(t => t.Kind == kind);
        return track?.Clips.FirstOrDefault(c => playhead >= c.TimelineStart && playhead <= c.TimelineEnd);
    }

    public bool SelectAt(TimeSpan playhead, TrackKind kind = TrackKind.Video)
    {
        var clip = ClipAt(playhead, kind);
        SelectedClipId = clip?.Id;
        return clip is not null;
    }

    /// <summary>Blade: split video (+ linked A1) clip at playhead.</summary>
    public bool SplitAt(TimeSpan playhead)
    {
        var changed = false;
        foreach (var track in Tracks.Where(t => t.Kind is TrackKind.Video or TrackKind.Audio))
        {
            for (var i = 0; i < track.Clips.Count; i++)
            {
                var c = track.Clips[i];
                if (playhead <= c.TimelineStart || playhead >= c.TimelineEnd)
                    continue;
                if (playhead - c.TimelineStart < TimeSpan.FromMilliseconds(120) ||
                    c.TimelineEnd - playhead < TimeSpan.FromMilliseconds(120))
                    continue;

                var offset = playhead - c.TimelineStart;
                var sourceCut = c.SourceIn + TimeSpan.FromSeconds(offset.TotalSeconds * c.Speed);

                var right = new TimelineClip
                {
                    SourcePath = c.SourcePath,
                    SourceIn = sourceCut,
                    SourceOut = c.SourceOut,
                    TimelineStart = playhead,
                    Speed = c.Speed,
                    Muted = c.Muted,
                    Opacity = c.Opacity,
                    TransitionIn = TransitionKind.None,
                    TransitionOut = c.TransitionOut,
                    FadeIn = TimeSpan.Zero,
                    FadeOut = c.FadeOut,
                    Overlay = c.Overlay,
                    Text = c.Text,
                    LutPath = c.LutPath
                };
                c.SourceOut = sourceCut;
                c.TransitionOut = TransitionKind.None;
                c.FadeOut = TimeSpan.Zero;
                track.Clips.Insert(i + 1, right);
                if (track.Kind == TrackKind.Video)
                    SelectedClipId = c.Id;
                changed = true;
                break;
            }
        }

        return changed;
    }

    /// <summary>
    /// Ripple-delete selected clip. If on V1, also remove matching A1 clip
    /// (same source in/out) and pack remaining V1+A1 left-to-right.
    /// </summary>
    public bool DeleteSelected()
    {
        if (SelectedClipId is null)
            return false;

        var id = SelectedClipId.Value;
        TimelineClip? removed = null;
        TimelineTrack? removedFrom = null;
        foreach (var track in Tracks)
        {
            var idx = track.Clips.FindIndex(c => c.Id == id);
            if (idx < 0)
                continue;
            removed = track.Clips[idx];
            removedFrom = track;
            track.Clips.RemoveAt(idx);
            break;
        }

        if (removed is null || removedFrom is null)
            return false;

        SelectedClipId = null;

        if (removedFrom.Kind == TrackKind.Video)
        {
            var a1 = Tracks.FirstOrDefault(t => t.Name == "A1");
            if (a1 is not null)
            {
                a1.Clips.RemoveAll(c =>
                    c.SourceIn == removed.SourceIn &&
                    c.SourceOut == removed.SourceOut &&
                    string.Equals(c.SourcePath, removed.SourcePath, StringComparison.OrdinalIgnoreCase));
            }

            PackLinkedVideoAudio();
        }

        return true;
    }

    /// <summary>Pack V1 and linked A1 clips abutting left-to-right (CapCut ripple).</summary>
    public void PackLinkedVideoAudio()
    {
        var v = VideoTrack;
        var a = Tracks.FirstOrDefault(t => t.Name == "A1");
        if (v is null)
            return;

        v.Clips.Sort((x, y) => x.TimelineStart.CompareTo(y.TimelineStart));
        var t = TimeSpan.Zero;
        foreach (var c in v.Clips)
        {
            c.TimelineStart = t;
            t += c.Duration;
        }

        if (a is null)
            return;

        // Rebuild A1 to mirror V1 ranges (keeps audio locked to video after drag/delete)
        a.Clips.Clear();
        foreach (var c in v.Clips.Where(x => x.Overlay == OverlayKind.None && !string.IsNullOrWhiteSpace(x.SourcePath)))
        {
            a.Clips.Add(new TimelineClip
            {
                SourcePath = c.SourcePath,
                SourceIn = c.SourceIn,
                SourceOut = c.SourceOut,
                TimelineStart = c.TimelineStart,
                Speed = c.Speed,
                Muted = c.Muted
            });
        }
    }

    public bool MoveClipToTrack(Guid clipId, TimelineTrack target)
    {
        TimelineClip? clip = null;
        TimelineTrack? from = null;
        foreach (var track in Tracks)
        {
            var found = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (found is null)
                continue;
            clip = found;
            from = track;
            break;
        }

        if (clip is null || from is null || ReferenceEquals(from, target))
            return false;

        from.Clips.Remove(clip);
        if (target.Kind == TrackKind.Audio)
            clip.Muted = target.Muted;
        target.Clips.Add(clip);
        target.Clips.Sort((a, b) => a.TimelineStart.CompareTo(b.TimelineStart));
        return true;
    }

    public void AddOverlay(OverlayKind kind, TimeSpan start, TimeSpan duration, string? text = null)
    {
        var ov = OverlayTrack;
        if (ov is null)
            return;

        ov.Clips.Add(new TimelineClip
        {
            SourcePath = "",
            SourceIn = TimeSpan.Zero,
            SourceOut = duration,
            TimelineStart = start,
            Overlay = kind,
            Text = text
        });
    }

    public void ImportClip(string path, TimeSpan duration, TrackKind kind = TrackKind.Video)
    {
        var track = Tracks.FirstOrDefault(t => t.Kind == kind) ?? VideoTrack;
        if (track is null)
            return;

        var start = track.Clips.Count == 0
            ? TimeSpan.Zero
            : track.Clips.Max(c => c.TimelineEnd);

        track.Clips.Add(new TimelineClip
        {
            SourcePath = path,
            SourceIn = TimeSpan.Zero,
            SourceOut = duration,
            TimelineStart = start
        });
    }

    /// <summary>Flatten V1 clips to kept source ranges (compat with legacy ExportKeptSegments).</summary>
    public IReadOnlyList<(TimeSpan Start, TimeSpan End)> ToKeptRanges()
    {
        var v = VideoTrack;
        if (v is null || v.Clips.Count == 0)
            return [];

        return v.Clips
            .Where(c => c.Overlay == OverlayKind.None && !string.IsNullOrWhiteSpace(c.SourcePath))
            .OrderBy(c => c.TimelineStart)
            .Select(c => (c.SourceIn, c.SourceOut))
            .ToList();
    }

    public void SyncFromKeptRanges(IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, string sourcePath)
    {
        var v = VideoTrack;
        var a = Tracks.FirstOrDefault(t => t.Name == "A1");
        if (v is null)
            return;

        // Keep fade/wipe/speed so Split→Wipe→Fade→Export does not reset the previous chip.
        var prior = v.Clips
            .Select(c => (c.SourcePath, c.SourceIn, c.SourceOut, c.Speed, c.FadeIn, c.FadeOut,
                c.TransitionIn, c.TransitionOut, c.LutPath, c.Opacity, c.Muted, c.Text))
            .ToList();

        v.Clips.Clear();
        a?.Clips.Clear();
        var t = TimeSpan.Zero;
        foreach (var (start, end) in kept)
        {
            var dur = end - start;
            if (dur <= TimeSpan.Zero)
                continue;
            var clip = new TimelineClip
            {
                SourcePath = sourcePath,
                SourceIn = start,
                SourceOut = end,
                TimelineStart = t
            };
            var match = prior.FindIndex(p =>
                p.SourceIn == start &&
                p.SourceOut == end &&
                string.Equals(p.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
            if (match >= 0)
            {
                var p = prior[match];
                clip.Speed = p.Speed;
                clip.FadeIn = p.FadeIn;
                clip.FadeOut = p.FadeOut;
                clip.TransitionIn = p.TransitionIn;
                clip.TransitionOut = p.TransitionOut;
                clip.LutPath = p.LutPath;
                clip.Opacity = p.Opacity;
                clip.Muted = p.Muted;
                clip.Text = p.Text;
                prior.RemoveAt(match);
            }

            v.Clips.Add(clip);
            a?.Clips.Add(new TimelineClip
            {
                SourcePath = sourcePath,
                SourceIn = start,
                SourceOut = end,
                TimelineStart = t
            });
            t += dur;
        }

        SelectedClipId = null;
    }

    /// <summary>
    /// Duration of an ffmpeg xfade join between two packed V1 clips.
    /// Wipe only — Fade stays a per-clip opacity filter.
    /// </summary>
    public static TimeSpan XfadeJoinDuration(TimelineClip left, TimelineClip right)
    {
        if (left.TransitionOut != TransitionKind.Wipe && right.TransitionIn != TransitionKind.Wipe)
            return TimeSpan.Zero;

        var requested = left.FadeOut > TimeSpan.Zero
            ? left.FadeOut
            : right.FadeIn > TimeSpan.Zero
                ? right.FadeIn
                : DefaultXfadeDuration;
        var capSec = Math.Min(left.Duration.TotalSeconds, right.Duration.TotalSeconds) * 0.4;
        if (capSec < 0.12)
            return TimeSpan.Zero;
        var cap = TimeSpan.FromSeconds(capSec);
        return requested > cap ? cap : requested;
    }

    /// <summary>ffmpeg xfade transition name for a wipe join.</summary>
    public const string XfadeWipeName = "wipeleft";

    /// <summary>After V1 reorder via kept-ranges sync, ensure A1 mirrors V1.</summary>
    public void EnsureAudioLinkedToVideo() => PackLinkedVideoAudio();
}
