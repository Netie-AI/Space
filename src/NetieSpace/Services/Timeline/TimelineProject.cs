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

    /// <summary>Sentinel V1 source for Leave-black packed holes (lavfi on export).</summary>
    public const string BlackSourcePath = ":black";

    public static bool IsBlackPlaceholder(string? path) =>
        string.Equals(path, BlackSourcePath, StringComparison.Ordinal);

    /// <summary>Sum of V1 media clips (no overlays). Wipe shortens the file later.</summary>
    public TimeSpan PackedVideoDuration()
    {
        var v = VideoTrack;
        if (v is null)
            return TimeSpan.Zero;
        var sec = 0.0;
        foreach (var c in v.Clips)
        {
            if (c.Overlay != OverlayKind.None)
                continue;
            sec += c.Duration.TotalSeconds;
        }

        return TimeSpan.FromSeconds(sec);
    }

    /// <summary>Latest overlay TimelineEnd, or zero.</summary>
    public TimeSpan OverlayEnd()
    {
        var ov = OverlayTrack;
        if (ov is null || ov.Clips.Count == 0)
            return TimeSpan.Zero;
        var max = TimeSpan.Zero;
        foreach (var c in ov.Clips)
        {
            if (c.TimelineEnd > max)
                max = c.TimelineEnd;
        }

        return max;
    }

    /// <summary>
    /// File length Export should target. Last-clip Leave black parks the
    /// title card past remaining V1; without this pad the overlay enable
    /// window is after EOF and the card never burns.
    /// </summary>
    public TimeSpan ExportDuration()
    {
        var v = PackedVideoDuration();
        var o = OverlayEnd();
        return o > v ? o : v;
    }

    /// <summary>Black frames to append so overlays past V1 still burn.</summary>
    public TimeSpan BlackPadAfterVideo()
    {
        var v = PackedVideoDuration();
        var o = OverlayEnd();
        return o > v ? o - v : TimeSpan.Zero;
    }

    /// <summary>True when V1 has clips from more than one source file.</summary>
    public bool HasMultipleVideoSources
    {
        get
        {
            var v = VideoTrack;
            if (v is null)
                return false;
            string? first = null;
            foreach (var c in v.Clips.Where(c =>
                         c.Overlay == OverlayKind.None &&
                         !string.IsNullOrWhiteSpace(c.SourcePath) &&
                         !IsBlackPlaceholder(c.SourcePath)))
            {
                if (first is null)
                {
                    first = c.SourcePath;
                    continue;
                }

                if (!string.Equals(first, c.SourcePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

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

    /// <summary>
    /// Deep copy for background export. Space-close + open another file
    /// resets the live project; a shared reference used to mutate mid-encode.
    /// </summary>
    public TimelineProject Clone()
    {
        var copy = new TimelineProject
        {
            FrameRate = FrameRate,
            Width = Width,
            Height = Height,
            ExportPreset = ExportPreset,
            GlobalLutPath = GlobalLutPath,
            SubtitleBurnPath = SubtitleBurnPath,
            FillGapsWithBlack = FillGapsWithBlack
        };
        copy.PrimarySourcePath = PrimarySourcePath;
        copy.SourceDuration = SourceDuration;
        foreach (var t in Tracks)
        {
            var nt = new TimelineTrack { Name = t.Name, Kind = t.Kind, Muted = t.Muted };
            foreach (var c in t.Clips)
                nt.Clips.Add(CopyClip(c));
            copy.Tracks.Add(nt);
        }
        return copy;
    }

    /// <summary>Copy overlay clips for the video undo stack (Leave-black title card).</summary>
    public List<TimelineClip> SnapshotOverlays()
    {
        var ov = OverlayTrack;
        if (ov is null)
            return [];
        return ov.Clips.Select(CopyClip).ToList();
    }

    /// <summary>
    /// Replace OV clips. Ctrl+Z after Leave black must remove that card or
    /// Export burns a leftover overlay on the restored clip.
    /// </summary>
    public void RestoreOverlays(IReadOnlyList<TimelineClip> clips)
    {
        var ov = OverlayTrack;
        if (ov is null)
        {
            ov = new TimelineTrack { Name = "OV", Kind = TrackKind.Overlay };
            Tracks.Add(ov);
        }

        ov.Clips.Clear();
        if (clips is null)
            return;
        foreach (var c in clips)
            ov.Clips.Add(CopyClip(c));
    }

    internal static TimelineClip CopyClip(TimelineClip c) =>
        new()
        {
            SourcePath = c.SourcePath,
            SourceIn = c.SourceIn,
            SourceOut = c.SourceOut,
            TimelineStart = c.TimelineStart,
            Speed = c.Speed,
            Muted = c.Muted,
            Opacity = c.Opacity,
            TransitionIn = c.TransitionIn,
            TransitionOut = c.TransitionOut,
            FadeIn = c.FadeIn,
            FadeOut = c.FadeOut,
            Overlay = c.Overlay,
            Text = c.Text,
            LutPath = c.LutPath
        };

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
        foreach (var c in v.Clips.Where(x =>
                     x.Overlay == OverlayKind.None &&
                     !string.IsNullOrWhiteSpace(x.SourcePath) &&
                     !IsBlackPlaceholder(x.SourcePath)))
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

    /// <summary>
    /// Pack a new clip onto the track after the last existing clip (no overlap).
    /// V1 also rebuilds linked A1. Drag reorder is how the user changes order later.
    /// </summary>
    public TimelineClip? ImportClip(string path, TimeSpan duration, TrackKind kind = TrackKind.Video)
    {
        var track = Tracks.FirstOrDefault(t => t.Kind == kind) ?? VideoTrack;
        if (track is null || string.IsNullOrWhiteSpace(path) || duration <= TimeSpan.Zero)
            return null;

        // Place after the rightmost clip so a second/third source never lands on top.
        var start = track.Clips.Count == 0
            ? TimeSpan.Zero
            : track.Clips.Max(c => c.TimelineEnd);

        var clip = new TimelineClip
        {
            SourcePath = path,
            SourceIn = TimeSpan.Zero,
            SourceOut = duration,
            TimelineStart = start
        };
        track.Clips.Add(clip);
        if (track.Kind == TrackKind.Video)
        {
            PackLinkedVideoAudio();
            SelectedClipId = clip.Id;
        }
        else
        {
            track.Clips.Sort((a, b) => a.TimelineStart.CompareTo(b.TimelineStart));
        }

        return clip;
    }

    /// <summary>
    /// Keep only [from, to) on the packed timeline, then ripple-pack.
    /// Export range after + Clip used to TrimAsync the open file.
    /// </summary>
    public bool SlicePacked(TimeSpan from, TimeSpan to)
    {
        if (to <= from)
            return false;
        var v = VideoTrack;
        if (v is null)
            return false;

        var next = new List<TimelineClip>();
        foreach (var c in v.Clips)
        {
            if (c.Overlay != OverlayKind.None)
                continue;
            var ovlIn = from > c.TimelineStart ? from : c.TimelineStart;
            var ovlOut = to < c.TimelineEnd ? to : c.TimelineEnd;
            if (ovlOut - ovlIn < TimeSpan.FromMilliseconds(40))
                continue;

            var speed = c.Speed <= 0 ? 1.0 : c.Speed;
            var srcShift = TimeSpan.FromSeconds((ovlIn - c.TimelineStart).TotalSeconds * speed);
            var srcLen = TimeSpan.FromSeconds((ovlOut - ovlIn).TotalSeconds * speed);
            var keepInEdge = ovlIn == c.TimelineStart;
            var keepOutEdge = ovlOut == c.TimelineEnd;
            next.Add(new TimelineClip
            {
                SourcePath = c.SourcePath,
                SourceIn = c.SourceIn + srcShift,
                SourceOut = c.SourceIn + srcShift + srcLen,
                TimelineStart = ovlIn - from,
                Speed = c.Speed,
                Muted = c.Muted,
                Opacity = c.Opacity,
                TransitionIn = keepInEdge ? c.TransitionIn : TransitionKind.None,
                TransitionOut = keepOutEdge ? c.TransitionOut : TransitionKind.None,
                FadeIn = keepInEdge ? c.FadeIn : default,
                FadeOut = keepOutEdge ? c.FadeOut : default,
                Overlay = c.Overlay,
                Text = c.Text,
                LutPath = c.LutPath
            });
        }

        if (next.Count == 0)
            return false;

        v.Clips.Clear();
        v.Clips.AddRange(next);

        if (OverlayTrack is { } ov)
        {
            var ovNext = new List<TimelineClip>();
            foreach (var c in ov.Clips)
            {
                var ovlIn = from > c.TimelineStart ? from : c.TimelineStart;
                var ovlOut = to < c.TimelineEnd ? to : c.TimelineEnd;
                if (ovlOut <= ovlIn)
                    continue;
                ovNext.Add(new TimelineClip
                {
                    SourcePath = c.SourcePath,
                    SourceIn = TimeSpan.Zero,
                    SourceOut = ovlOut - ovlIn,
                    TimelineStart = ovlIn - from,
                    Overlay = c.Overlay,
                    Text = c.Text,
                    FadeIn = c.FadeIn,
                    FadeOut = c.FadeOut,
                    TransitionIn = c.TransitionIn,
                    TransitionOut = c.TransitionOut,
                    Opacity = c.Opacity
                });
            }
            ov.Clips.Clear();
            ov.Clips.AddRange(ovNext);
        }

        PackLinkedVideoAudio();
        SliceSubtitleBurn(from, to);
        return true;
    }

    /// <summary>
    /// Caption cue times are packed seconds (same ruler as Export range).
    /// SlicePacked used to keep the original SRT, so a clip-2 range burned
    /// clip-1 text. Writes a temp SRT; does not mutate the user's file.
    /// </summary>
    private void SliceSubtitleBurn(TimeSpan from, TimeSpan to)
    {
        if (string.IsNullOrWhiteSpace(SubtitleBurnPath) || !IoFile.Exists(SubtitleBurnPath))
            return;

        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var c in ReadSrtCues(SubtitleBurnPath))
        {
            var ovlIn = from > c.Start ? from : c.Start;
            var ovlOut = to < c.End ? to : c.End;
            if (ovlOut - ovlIn < TimeSpan.FromMilliseconds(40))
                continue;
            next.Add((ovlIn - from, ovlOut - from, c.Text));
        }

        if (next.Count == 0)
        {
            SubtitleBurnPath = null;
            return;
        }

        var temp = IoPath.Combine(IoPath.GetTempPath(), $"_ns_range_{Guid.NewGuid():N}.srt");
        WriteSrtCues(temp, next);
        SubtitleBurnPath = temp;
    }

    /// <summary>
    /// Map source-time SRT cues onto packed seconds for one V1 clip.
    /// Auto captions after + Clip used to whisper only the open file, so
    /// clip 2 stayed silent on Export.
    /// </summary>
    public static List<(TimeSpan Start, TimeSpan End, string Text)> MapCuesToPacked(
        IEnumerable<(TimeSpan Start, TimeSpan End, string Text)> cues,
        TimeSpan sourceIn,
        TimeSpan sourceOut,
        double speed,
        TimeSpan packedStart)
    {
        if (speed <= 0)
            speed = 1;
        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var c in cues)
        {
            var ovlIn = sourceIn > c.Start ? sourceIn : c.Start;
            var ovlOut = sourceOut < c.End ? sourceOut : c.End;
            if (ovlOut - ovlIn < TimeSpan.FromMilliseconds(40))
                continue;
            var packedIn = packedStart + TimeSpan.FromSeconds((ovlIn - sourceIn).TotalSeconds / speed);
            var packedOut = packedStart + TimeSpan.FromSeconds((ovlOut - sourceIn).TotalSeconds / speed);
            next.Add((packedIn, packedOut, c.Text));
        }

        return next;
    }

    /// <summary>
    /// Caption cues are packed seconds. Speed used to leave them on the old
    /// ruler, so a 2x clip burned clip-1 text onto clip 2. Does not mutate
    /// the user's sidecar -- caller writes a temp SRT.
    /// </summary>
    public static List<(TimeSpan Start, TimeSpan End, string Text)> RemapPackedCuesAfterSpeed(
        IEnumerable<(TimeSpan Start, TimeSpan End, string Text)> cues,
        TimeSpan packedStart,
        TimeSpan oldPackedEnd,
        double oldSpeed,
        double newSpeed)
    {
        if (oldSpeed <= 0)
            oldSpeed = 1;
        if (newSpeed <= 0)
            newSpeed = 1;
        var oldLen = oldPackedEnd - packedStart;
        if (oldLen <= TimeSpan.Zero)
            return cues.ToList();
        var newLen = TimeSpan.FromSeconds(oldLen.TotalSeconds * (oldSpeed / newSpeed));
        var delta = newLen - oldLen;
        var scale = oldSpeed / newSpeed;
        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var c in cues)
        {
            if (c.Start >= oldPackedEnd)
            {
                next.Add((c.Start + delta, c.End + delta, c.Text));
                continue;
            }

            if (c.End <= packedStart)
            {
                next.Add(c);
                continue;
            }

            var start = c.Start >= packedStart
                ? packedStart + TimeSpan.FromSeconds((c.Start - packedStart).TotalSeconds * scale)
                : c.Start;
            var end = c.End <= oldPackedEnd
                ? packedStart + TimeSpan.FromSeconds((c.End - packedStart).TotalSeconds * scale)
                : c.End + delta;
            if (end - start < TimeSpan.FromMilliseconds(40))
                continue;
            next.Add((start, end, c.Text));
        }

        return next;
    }

    /// <summary>
    /// Caption cues are packed seconds. Drag / Order / ripple used to leave
    /// them on the old ruler, so a swapped clip-2 still burned at 1.8s.
    /// Cues on a deleted clip are dropped.
    /// </summary>
    public static List<(TimeSpan Start, TimeSpan End, string Text)> RemapPackedCuesAfterRepack(
        IEnumerable<(TimeSpan Start, TimeSpan End, string Text)> cues,
        IReadOnlyList<(string SourcePath, TimeSpan SourceIn, TimeSpan SourceOut, double Speed, TimeSpan PackedStart, TimeSpan PackedEnd)> before,
        IReadOnlyList<(string SourcePath, TimeSpan SourceIn, TimeSpan SourceOut, double Speed, TimeSpan PackedStart, TimeSpan PackedEnd)> after)
    {
        var pairs = new List<(TimeSpan OldStart, TimeSpan OldEnd, TimeSpan NewStart)>();
        var used = new bool[after.Count];
        foreach (var b in before)
        {
            for (var i = 0; i < after.Count; i++)
            {
                if (used[i])
                    continue;
                var a = after[i];
                if (!string.Equals(a.SourcePath, b.SourcePath, StringComparison.OrdinalIgnoreCase) ||
                    a.SourceIn != b.SourceIn ||
                    a.SourceOut != b.SourceOut ||
                    Math.Abs(a.Speed - b.Speed) > 0.01)
                    continue;
                used[i] = true;
                pairs.Add((b.PackedStart, b.PackedEnd, a.PackedStart));
                break;
            }
        }

        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var c in cues)
        {
            var hit = false;
            foreach (var p in pairs)
            {
                if (c.Start < p.OldStart || c.Start >= p.OldEnd)
                    continue;
                var delta = p.NewStart - p.OldStart;
                next.Add((c.Start + delta, c.End + delta, c.Text));
                hit = true;
                break;
            }

            if (!hit)
                continue;
        }

        next.Sort((a, b) => a.Start.CompareTo(b.Start));
        return next;
    }

    /// <summary>
    /// UI packed time ignores wipe. Export xfade shortens the file by each
    /// join, so a clip-2 cue at 1.8s burned 0.5s late (0.7s into clip 2).
    /// </summary>
    public static TimeSpan WipeExportShift(IReadOnlyList<TimelineClip> clips, TimeSpan packedTime)
    {
        var shift = TimeSpan.Zero;
        if (clips is null || clips.Count < 2)
            return shift;
        for (var i = 0; i < clips.Count - 1; i++)
        {
            var xfade = XfadeJoinDuration(clips[i], clips[i + 1]);
            if (xfade <= TimeSpan.Zero)
                continue;
            if (packedTime >= clips[i + 1].TimelineStart)
                shift += xfade;
        }

        return shift;
    }

    /// <summary>
    /// Shift packed cue times onto the wipe-shortened export clock.
    /// Does not mutate the user's sidecar -- caller writes a temp SRT.
    /// </summary>
    public static List<(TimeSpan Start, TimeSpan End, string Text)> RemapPackedCuesAfterWipe(
        IEnumerable<(TimeSpan Start, TimeSpan End, string Text)> cues,
        IReadOnlyList<TimelineClip> clips)
    {
        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var c in cues)
        {
            var start = c.Start - WipeExportShift(clips, c.Start);
            var end = c.End - WipeExportShift(clips, c.End);
            if (start < TimeSpan.Zero)
                start = TimeSpan.Zero;
            if (end - start < TimeSpan.FromMilliseconds(40))
                continue;
            next.Add((start, end, c.Text));
        }

        return next;
    }

    /// <summary>
    /// Export clone only. UI packed ruler stays pre-wipe (marks / Split).
    /// Writes a temp SRT and shifts overlay TimelineStart by the same joins.
    /// </summary>
    public void ApplyWipeExportRemap()
    {
        var v = VideoTrack?.Clips;
        if (v is null || v.Count < 2)
            return;
        var hasWipe = false;
        for (var i = 0; i < v.Count - 1; i++)
        {
            if (XfadeJoinDuration(v[i], v[i + 1]) > TimeSpan.Zero)
            {
                hasWipe = true;
                break;
            }
        }

        if (!hasWipe)
            return;

        if (!string.IsNullOrWhiteSpace(SubtitleBurnPath) && IoFile.Exists(SubtitleBurnPath))
        {
            var remapped = RemapPackedCuesAfterWipe(ReadSrtCues(SubtitleBurnPath), v);
            if (remapped.Count == 0)
                SubtitleBurnPath = null;
            else
            {
                var temp = IoPath.Combine(IoPath.GetTempPath(), $"_ns_wipe_{Guid.NewGuid():N}.srt");
                WriteSrtCues(temp, remapped);
                SubtitleBurnPath = temp;
            }
        }

        if (OverlayTrack is { Clips.Count: > 0 } ov)
        {
            foreach (var o in ov.Clips)
            {
                var shift = WipeExportShift(v, o.TimelineStart);
                if (shift > TimeSpan.Zero)
                    o.TimelineStart -= shift;
            }
        }
    }

    /// <summary>
    /// Merge per-clip source SRTs into one packed-time sidecar. Returns cue count.
    /// </summary>
    public static int WritePackedCaptions(
        string outputPath,
        IReadOnlyList<(string SrtPath, TimeSpan SourceIn, TimeSpan SourceOut, double Speed, TimeSpan PackedStart)> clips)
    {
        var next = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        foreach (var clip in clips)
        {
            if (string.IsNullOrWhiteSpace(clip.SrtPath) || !IoFile.Exists(clip.SrtPath))
                continue;
            next.AddRange(MapCuesToPacked(
                ReadSrtCues(clip.SrtPath),
                clip.SourceIn,
                clip.SourceOut,
                clip.Speed,
                clip.PackedStart));
        }

        next.Sort((a, b) => a.Start.CompareTo(b.Start));
        if (next.Count == 0)
            return 0;
        WriteSrtCues(outputPath, next);
        return next.Count;
    }

    public static List<(TimeSpan Start, TimeSpan End, string Text)> ReadSrtCues(string path)
    {
        var cues = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        var blocks = IoFile.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                continue;
            var arrow = lines.FirstOrDefault(l => l.Contains("-->", StringComparison.Ordinal));
            if (arrow is null)
                continue;
            var parts = arrow.Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;
            if (!TryParseSrtTs(parts[0], out var start) || !TryParseSrtTs(parts[1], out var end))
                continue;
            var textIdx = Array.IndexOf(lines, arrow) + 1;
            var text = string.Join(" ", lines.Skip(Math.Max(1, textIdx))).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            cues.Add((start, end, text));
        }

        return cues;
    }

    public static void WriteSrtCues(string path, List<(TimeSpan Start, TimeSpan End, string Text)> cues)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            var c = cues[i];
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatSrtTs(c.Start)} --> {FormatSrtTs(c.End)}");
            sb.AppendLine(c.Text);
            sb.AppendLine();
        }

        IoFile.WriteAllText(path, sb.ToString());
    }

    private static bool TryParseSrtTs(string raw, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        raw = raw.Trim().Replace(',', '.');
        return TimeSpan.TryParse(raw, out ts);
    }

    private static string FormatSrtTs(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    /// <summary>Flatten V1 clips to kept source ranges (compat with legacy ExportKeptSegments).</summary>
    public IReadOnlyList<(TimeSpan Start, TimeSpan End)> ToKeptRanges()
    {
        var v = VideoTrack;
        if (v is null || v.Clips.Count == 0)
            return [];

        return v.Clips
            .Where(c =>
                c.Overlay == OverlayKind.None &&
                !string.IsNullOrWhiteSpace(c.SourcePath) &&
                !IsBlackPlaceholder(c.SourcePath))
            .OrderBy(c => c.TimelineStart)
            .Select(c => (c.SourceIn, c.SourceOut))
            .ToList();
    }

    public void SyncFromKeptRanges(IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, string sourcePath)
    {
        var rows = kept.Select(k => (k.Start, k.End, sourcePath)).ToList();
        SyncFromKeptRanges(rows, sourcePath);
    }

    public void SyncFromKeptRanges(
        IReadOnlyList<(TimeSpan Start, TimeSpan End, string SourcePath)> kept,
        string fallbackSource)
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
        foreach (var (start, end, rawPath) in kept)
        {
            var dur = end - start;
            if (dur <= TimeSpan.Zero)
                continue;
            var path = string.IsNullOrWhiteSpace(rawPath) ? fallbackSource : rawPath;
            var clip = new TimelineClip
            {
                SourcePath = path,
                SourceIn = start,
                SourceOut = end,
                TimelineStart = t
            };
            var match = prior.FindIndex(p =>
                p.SourceIn == start &&
                p.SourceOut == end &&
                string.Equals(p.SourcePath, path, StringComparison.OrdinalIgnoreCase));
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
                SourcePath = path,
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
