using NetieSpace.Services.Timeline;

namespace NetieSpace.Services;

/// <summary>
/// CapCut / Premiere–light: packed V1 keep list (one or more source files).
/// Split = blade; Delete = ripple-remove; + Clip = pack on the end (no overlap).
/// </summary>
public sealed class VideoEditSession
{
    private readonly List<KeptClip> _kept = [];
    private readonly Stack<List<KeptClip>> _undo = new();

    public IReadOnlyList<(TimeSpan Start, TimeSpan End)> Kept =>
        _kept.ConvertAll(c => (c.Start, c.End));

    public IReadOnlyList<KeptClip> Clips => _kept;

    public string PrimarySourcePath { get; private set; } = "";

    public TimeSpan SourceDuration { get; private set; }

    /// <summary>Selected clip index for Delete; -1 = none.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < _kept.Count;

    /// <summary>When true, deleted gaps render/export as black fillers (CapCut-style).</summary>
    public bool FillGapsWithBlack { get; set; } = false;

    /// <summary>True when the selected clip has a following clip to wipe/crossfade into.</summary>
    public bool CanJoinToNext => HasSelection && SelectedIndex < _kept.Count - 1;

    /// <summary>
    /// Left clip index for a wipe at the selected join. Works from either side
    /// (playbook: "Wipe on the join"). Null if only one clip.
    /// </summary>
    public int? WipeJoinIndex()
    {
        if (!HasSelection || _kept.Count < 2)
            return null;
        if (CanJoinToNext)
            return SelectedIndex;
        if (SelectedIndex > 0)
            return SelectedIndex - 1;
        return null;
    }

    public bool HasMultipleSources
    {
        get
        {
            string? first = null;
            foreach (var c in _kept)
            {
                var path = ResolvedSource(c.SourcePath);
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                if (first is null)
                {
                    first = path;
                    continue;
                }

                if (!string.Equals(first, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    public void Reset(TimeSpan duration, string? sourcePath = null)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
            PrimarySourcePath = sourcePath;
        SourceDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _kept.Clear();
        _undo.Clear();
        SelectedIndex = -1;
        if (SourceDuration > TimeSpan.Zero)
            _kept.Add(new KeptClip(TimeSpan.Zero, SourceDuration, PrimarySourcePath));
    }

    public void Select(int index)
    {
        SelectedIndex = index >= 0 && index < _kept.Count ? index : -1;
    }

    public void ClearSelection() => SelectedIndex = -1;

    /// <summary>Restore kept ranges from undo snapshot (single-source fallback).</summary>
    public void RestoreKept(IReadOnlyList<(TimeSpan Start, TimeSpan End)> ranges)
    {
        RestoreKept(ranges.Select(r => new KeptClip(r.Start, r.End, PrimarySourcePath)).ToList());
    }

    public void RestoreKept(IReadOnlyList<KeptClip> clips)
    {
        _kept.Clear();
        _kept.AddRange(clips.Where(r => r.End - r.Start >= TimeSpan.FromMilliseconds(40)));
        SelectedIndex = -1;
    }

    public void PushUndo()
    {
        _undo.Push(_kept.Select(c => c).ToList());
        if (_undo.Count <= 40)
            return;
        var keep = _undo.Take(30).Reverse().ToList();
        _undo.Clear();
        foreach (var s in keep.AsEnumerable().Reverse())
            _undo.Push(s);
    }

    public bool TryUndo()
    {
        if (_undo.Count == 0)
            return false;
        RestoreKept(_undo.Pop());
        return true;
    }

    /// <summary>
    /// Pack a second/third video onto V1 after existing clips (no overlap).
    /// Drag reorder still uses <see cref="MoveClip"/>.
    /// </summary>
    public int AppendClip(string sourcePath, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || duration < TimeSpan.FromMilliseconds(40))
            return -1;
        _kept.Add(new KeptClip(TimeSpan.Zero, duration, sourcePath));
        SelectedIndex = _kept.Count - 1;
        return SelectedIndex;
    }

    /// <summary>Push packed clips (with per-clip sources) onto the NLE project.</summary>
    public void ApplyTo(TimelineProject project, string fallbackSource)
    {
        var fallback = string.IsNullOrWhiteSpace(fallbackSource) ? PrimarySourcePath : fallbackSource;
        var rows = _kept.Select(c =>
        {
            var path = string.IsNullOrWhiteSpace(c.SourcePath) ? fallback : c.SourcePath;
            return (c.Start, c.End, path);
        }).ToList();
        project.SyncFromKeptRanges(rows, fallback);
        var v = project.VideoTrack;
        if (v is null)
            return;
        for (var i = 0; i < _kept.Count && i < v.Clips.Count; i++)
        {
            var k = _kept[i];
            v.Clips[i].FadeIn = k.FadeIn;
            v.Clips[i].FadeOut = k.FadeOut;
            v.Clips[i].TransitionIn = k.TransitionIn;
            v.Clips[i].TransitionOut = k.TransitionOut;
        }
    }

    /// <summary>Mark the join at the selection as wipeleft. Either adjacent clip works.</summary>
    public bool ApplyWipeAtJoin()
    {
        var left = WipeJoinIndex();
        if (left is not { } i || i + 1 >= _kept.Count)
            return false;
        var fade = TimeSpan.FromMilliseconds(500);
        var a = _kept[i];
        var b = _kept[i + 1];
        _kept[i] = a with
        {
            TransitionOut = TransitionKind.Wipe,
            FadeOut = a.FadeOut > TimeSpan.Zero ? a.FadeOut : fade
        };
        _kept[i + 1] = b with { TransitionIn = TransitionKind.Wipe };
        return true;
    }

    public bool ApplyFadeAt(int index, TimeSpan? fadeIn, TimeSpan? fadeOut)
    {
        if (index < 0 || index >= _kept.Count)
            return false;
        var c = _kept[index];
        var nextIn = fadeIn ?? c.FadeIn;
        var nextOut = fadeOut ?? c.FadeOut;
        if (nextIn == c.FadeIn && nextOut == c.FadeOut)
            return false;
        _kept[index] = c with { FadeIn = nextIn, FadeOut = nextOut };
        return true;
    }

    /// <summary>Select clip under playhead (or nearest containing range).</summary>
    public int SelectAt(TimeSpan playhead)
    {
        var idx = IndexAt(playhead);
        SelectedIndex = idx;
        return idx;
    }

    public bool SplitAt(TimeSpan playhead)
    {
        var i = PickIndexForSourcePlayhead(playhead);
        if (i < 0)
            return false;

        var clip = _kept[i];
        if (playhead - clip.Start < TimeSpan.FromMilliseconds(120) ||
            clip.End - playhead < TimeSpan.FromMilliseconds(120))
            return false;

        ApplyBlade(i, clip, playhead);
        return true;
    }

    /// <summary>Blade the clip under a packed-timeline playhead (multi-source safe).</summary>
    public bool SplitAtPacked(TimeSpan packedTime)
    {
        var segs = BuildPackedTimeline(fillSourceGapsWithBlack: false);
        foreach (var s in segs)
        {
            if (s.IsBlack)
                continue;
            if (packedTime <= s.TimelineIn || packedTime >= s.TimelineOut)
                continue;
            if (packedTime - s.TimelineIn < TimeSpan.FromMilliseconds(120) ||
                s.TimelineOut - packedTime < TimeSpan.FromMilliseconds(120))
                return false;

            var cut = s.SourceIn + (packedTime - s.TimelineIn);
            return SplitAtIndex(s.ClipIndex, cut);
        }

        return false;
    }

    private bool SplitAtIndex(int i, TimeSpan playhead)
    {
        if (i < 0 || i >= _kept.Count)
            return false;
        var clip = _kept[i];
        if (playhead <= clip.Start || playhead >= clip.End)
            return false;
        if (playhead - clip.Start < TimeSpan.FromMilliseconds(120) ||
            clip.End - playhead < TimeSpan.FromMilliseconds(120))
            return false;
        ApplyBlade(i, clip, playhead);
        return true;
    }

    /// <summary>
    /// Same blade as TimelineProject.SplitAt: wipe/fade-out stay on the
    /// outgoing half, wipe/fade-in stay on the incoming half. A raw
    /// <c>with</c> copy put wipe on both halves and burned a second xfade.
    /// </summary>
    private void ApplyBlade(int i, KeptClip clip, TimeSpan playhead)
    {
        _kept[i] = clip with
        {
            End = playhead,
            TransitionOut = TransitionKind.None,
            FadeOut = default
        };
        _kept.Insert(i + 1, clip with
        {
            Start = playhead,
            TransitionIn = TransitionKind.None,
            FadeIn = default
        });
        SelectedIndex = i;
    }

    /// <summary>Ripple-delete the selected clip, or the segment that contains playhead.</summary>
    public bool DeleteSelectedOrAt(TimeSpan playhead)
    {
        if (HasSelection)
        {
            _kept.RemoveAt(SelectedIndex);
            SelectedIndex = -1;
            return true;
        }

        return DeleteSegmentAt(playhead);
    }

    /// <summary>Ripple-delete the kept segment that contains playhead (CapCut select→Delete).</summary>
    public bool DeleteSegmentAt(TimeSpan playhead)
    {
        var i = PickIndexForSourcePlayhead(playhead);
        if (i < 0)
            return false;
        _kept.RemoveAt(i);
        if (SelectedIndex == i)
            SelectedIndex = -1;
        else if (SelectedIndex > i)
            SelectedIndex--;
        return true;
    }

    /// <summary>Ripple-delete source range [from,to) from all kept segments (Premiere lift/extract light).</summary>
    public bool DeleteRange(TimeSpan from, TimeSpan to)
    {
        if (to <= from)
            return false;

        var next = new List<KeptClip>();
        var changed = false;
        foreach (var c in _kept)
        {
            if (c.End <= from || c.Start >= to)
            {
                next.Add(c);
                continue;
            }

            changed = true;
            if (c.Start < from)
                next.Add(c with { End = from });
            if (c.End > to)
                next.Add(c with { Start = to });
        }

        if (!changed)
            return false;

        _kept.Clear();
        _kept.AddRange(next.Where(s => s.End - s.Start >= TimeSpan.FromMilliseconds(80)));
        SelectedIndex = -1;
        return true;
    }

    /// <summary>
    /// Packed timeline start of a kept clip. <see cref="KeptClip.Start"/> is the
    /// source in-point (often 0 on + Clip), so a black title card must not use it.
    /// </summary>
    public TimeSpan PackedStartOf(int index)
    {
        if (index < 0 || index >= _kept.Count)
            return TimeSpan.Zero;
        foreach (var s in BuildPackedTimeline(false))
        {
            if (!s.IsBlack && s.ClipIndex == index)
                return s.TimelineIn;
        }

        return TimeSpan.Zero;
    }

    /// <summary>Packed timeline end of a kept clip (start + source duration).</summary>
    public TimeSpan PackedEndOf(int index)
    {
        if (index < 0 || index >= _kept.Count)
            return PackedStartOf(index);
        return PackedStartOf(index) + (_kept[index].End - _kept[index].Start);
    }

    public int IndexAt(TimeSpan playhead) => PickIndexForSourcePlayhead(playhead);

    /// <summary>Hit-test a normalized 0–1 packed timeline position.</summary>
    public int IndexAtPackedNormalized(double t01, bool? fillGaps = null)
    {
        var packed = PackedDuration(fillGaps ?? FillGapsWithBlack);
        if (packed <= TimeSpan.Zero)
            return -1;
        return IndexAtPacked(TimeSpan.FromSeconds(Math.Clamp(t01, 0, 1) * packed.TotalSeconds), fillGaps ?? FillGapsWithBlack);
    }

    /// <summary>Map packed timeline seconds → source playhead for preview.</summary>
    public TimeSpan PackedToSource(TimeSpan packedTime, bool? fillGaps = null)
    {
        foreach (var s in BuildPackedTimeline(fillGaps ?? FillGapsWithBlack))
        {
            if (packedTime >= s.TimelineIn && packedTime < s.TimelineOut)
            {
                if (s.IsBlack)
                    return s.SourceIn;
                return s.SourceIn + (packedTime - s.TimelineIn);
            }
        }

        if (_kept.Count > 0)
            return _kept[^1].End;
        return TimeSpan.Zero;
    }

    /// <summary>Map source playhead → packed timeline position for slider.</summary>
    public TimeSpan SourceToPacked(TimeSpan sourceTime, bool? fillGaps = null) =>
        SourceToPacked(sourceTime, PrimarySourcePath, fillGaps);

    /// <summary>
    /// Map a source time on <paramref name="sourcePath"/> to packed seconds.
    /// The open-file overload only sees the primary path, so a + Clip seek
    /// into clip 2 must pass that file or the slider snaps back to clip 1.
    /// </summary>
    public TimeSpan SourceToPacked(TimeSpan sourceTime, string? sourcePath, bool? fillGaps = null)
    {
        var want = string.IsNullOrWhiteSpace(sourcePath) ? PrimarySourcePath : sourcePath;
        foreach (var s in BuildPackedTimeline(fillGaps ?? FillGapsWithBlack))
        {
            if (s.IsBlack)
                continue;
            if (!ClipSourceEquals(s.ClipIndex, want))
                continue;
            if (sourceTime >= s.SourceIn && sourceTime <= s.SourceOut)
                return s.TimelineIn + (sourceTime - s.SourceIn);
        }

        return sourceTime;
    }

    /// <summary>
    /// Map a Mark In/Out source time to packed seconds. After + Clip,
    /// marks are source times on the selected clip; primary-only
    /// <see cref="SourceToPacked(TimeSpan, bool?)"/> draws them on clip 1.
    /// </summary>
    public TimeSpan MarkToPacked(TimeSpan mark, bool? fillGaps = null)
    {
        if (HasSelection)
            return SourceToPacked(mark, _kept[SelectedIndex].SourcePath, fillGaps);
        return SourceToPacked(mark, fillGaps);
    }

    /// <summary>Hit-test a normalized 0–1 timeline position against kept segments (source time).</summary>
    public int IndexAtNormalized(double t01)
    {
        if (SourceDuration <= TimeSpan.Zero)
            return -1;
        var playhead = TimeSpan.FromSeconds(Math.Clamp(t01, 0, 1) * SourceDuration.TotalSeconds);
        return IndexAt(playhead);
    }

    public string Describe() =>
        _kept.Count == 0
            ? "No clips (all deleted)"
            : string.Join(" · ", _kept.Select((s, i) =>
            {
                var name = string.IsNullOrWhiteSpace(s.SourcePath)
                    ? ""
                    : $" {IoPath.GetFileName(s.SourcePath)}";
                return $"{(i == SelectedIndex ? "▸" : "")}[{i + 1}]{name} {Format(s.Start)}–{Format(s.End)}";
            }));

    /// <summary>
    /// Replace packed V1 order. <paramref name="fromIndices"/> is a permutation of 0..Count-1
    /// (index in the current list). Used by the clip-order dialog.
    /// </summary>
    public bool ApplyOrder(IReadOnlyList<int> fromIndices)
    {
        if (fromIndices.Count != _kept.Count || _kept.Count == 0)
            return false;
        var seen = new bool[_kept.Count];
        var next = new List<KeptClip>(_kept.Count);
        foreach (var i in fromIndices)
        {
            if (i < 0 || i >= _kept.Count || seen[i])
                return false;
            seen[i] = true;
            next.Add(_kept[i]);
        }

        _kept.Clear();
        _kept.AddRange(next);
        SelectedIndex = 0;
        return true;
    }

    /// <summary>Reorder clip from one index to another (CapCut drag).</summary>
    public bool MoveClip(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _kept.Count)
            return false;
        toIndex = Math.Clamp(toIndex, 0, _kept.Count - 1);
        if (fromIndex == toIndex)
            return false;
        var item = _kept[fromIndex];
        _kept.RemoveAt(fromIndex);
        _kept.Insert(toIndex, item);
        SelectedIndex = toIndex;
        return true;
    }

    /// <summary>
    /// Packed output layout: clips abut left-to-right in list order (CapCut ripple).
    /// Optional black fillers only for holes in the *same* source between consecutive kept ranges.
    /// </summary>
    public IReadOnlyList<TimelineSegment> BuildPackedTimeline(bool fillSourceGapsWithBlack = true)
    {
        var list = new List<TimelineSegment>();
        var cursor = TimeSpan.Zero;
        for (var i = 0; i < _kept.Count; i++)
        {
            var a = _kept[i].Start;
            var b = _kept[i].End;
            if (fillSourceGapsWithBlack && i > 0 && ClipSourceEquals(i, _kept[i - 1].SourcePath))
            {
                var prevEnd = _kept[i - 1].End;
                if (a > prevEnd + TimeSpan.FromMilliseconds(40))
                {
                    var gap = a - prevEnd;
                    list.Add(new TimelineSegment(i - 1, cursor, cursor + gap, prevEnd, a, IsBlack: true));
                    cursor += gap;
                }
            }

            var dur = b - a;
            if (dur <= TimeSpan.Zero)
                continue;
            list.Add(new TimelineSegment(i, cursor, cursor + dur, a, b, IsBlack: false));
            cursor += dur;
        }

        return list;
    }

    public TimeSpan PackedDuration(bool fillGaps = true)
    {
        var segs = BuildPackedTimeline(fillGaps);
        return segs.Count == 0 ? TimeSpan.Zero : segs[^1].TimelineOut;
    }

    /// <summary>Hit-test packed timeline position → clip index (-1 = black filler / miss).</summary>
    public int IndexAtPacked(TimeSpan packedTime, bool fillGaps = true)
    {
        foreach (var s in BuildPackedTimeline(fillGaps))
        {
            if (s.IsBlack)
                continue;
            if (packedTime >= s.TimelineIn && packedTime < s.TimelineOut)
                return s.ClipIndex;
        }

        return -1;
    }

    private int PickIndexForSourcePlayhead(TimeSpan playhead)
    {
        var matches = new List<int>();
        for (var i = 0; i < _kept.Count; i++)
        {
            var c = _kept[i];
            if (playhead >= c.Start && playhead <= c.End)
                matches.Add(i);
        }

        if (matches.Count == 0)
            return -1;
        if (matches.Count == 1)
            return matches[0];

        foreach (var i in matches)
        {
            if (ClipSourceEquals(i, PrimarySourcePath))
                return i;
        }

        if (HasSelection && matches.Contains(SelectedIndex))
            return SelectedIndex;
        return matches[0];
    }

    private bool ClipSourceEquals(int index, string? path)
    {
        if (index < 0 || index >= _kept.Count)
            return false;
        return string.Equals(ResolvedSource(_kept[index].SourcePath), ResolvedSource(path),
            StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvedSource(string? path) =>
        string.IsNullOrWhiteSpace(path) ? PrimarySourcePath : path;

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}

public readonly record struct KeptClip(
    TimeSpan Start,
    TimeSpan End,
    string SourcePath,
    TimeSpan FadeIn = default,
    TimeSpan FadeOut = default,
    TransitionKind TransitionIn = TransitionKind.None,
    TransitionKind TransitionOut = TransitionKind.None);

public readonly record struct TimelineSegment(
    int ClipIndex,
    TimeSpan TimelineIn,
    TimeSpan TimelineOut,
    TimeSpan SourceIn,
    TimeSpan SourceOut,
    bool IsBlack);
