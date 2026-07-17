using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// CapCut / Premiere–light: one track of kept source ranges.
/// Split = blade at playhead; Delete = ripple-remove selected clip; Export = ffmpeg concat.
/// </summary>
public sealed class VideoEditSession
{
    private readonly List<(TimeSpan Start, TimeSpan End)> _kept = [];

    public IReadOnlyList<(TimeSpan Start, TimeSpan End)> Kept => _kept;
    public TimeSpan SourceDuration { get; private set; }

    /// <summary>Selected clip index for Delete; -1 = none.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < _kept.Count;

    /// <summary>When true, deleted gaps render/export as black fillers (CapCut-style).</summary>
    public bool FillGapsWithBlack { get; set; } = false;

    public void Reset(TimeSpan duration)
    {
        SourceDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _kept.Clear();
        SelectedIndex = -1;
        if (SourceDuration > TimeSpan.Zero)
            _kept.Add((TimeSpan.Zero, SourceDuration));
    }

    public void Select(int index)
    {
        SelectedIndex = index >= 0 && index < _kept.Count ? index : -1;
    }

    public void ClearSelection() => SelectedIndex = -1;

    /// <summary>Restore kept ranges from undo snapshot.</summary>
    public void RestoreKept(IReadOnlyList<(TimeSpan Start, TimeSpan End)> ranges)
    {
        _kept.Clear();
        _kept.AddRange(ranges.Where(r => r.End - r.Start >= TimeSpan.FromMilliseconds(40)));
        SelectedIndex = -1;
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
        if (playhead <= TimeSpan.Zero || playhead >= SourceDuration)
            return false;

        for (var i = 0; i < _kept.Count; i++)
        {
            var (a, b) = _kept[i];
            if (playhead <= a || playhead >= b)
                continue;
            if (playhead - a < TimeSpan.FromMilliseconds(120) ||
                b - playhead < TimeSpan.FromMilliseconds(120))
                return false;

            _kept[i] = (a, playhead);
            _kept.Insert(i + 1, (playhead, b));
            SelectedIndex = i; // left half selected after blade
            return true;
        }

        return false;
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
        for (var i = 0; i < _kept.Count; i++)
        {
            var (a, b) = _kept[i];
            if (playhead < a || playhead > b)
                continue;
            if (playhead == b && i + 1 < _kept.Count && _kept[i + 1].Start == b)
                continue;
            _kept.RemoveAt(i);
            if (SelectedIndex == i)
                SelectedIndex = -1;
            else if (SelectedIndex > i)
                SelectedIndex--;
            return true;
        }

        return false;
    }

    /// <summary>Ripple-delete source range [from,to) from all kept segments (Premiere lift/extract light).</summary>
    public bool DeleteRange(TimeSpan from, TimeSpan to)
    {
        if (to <= from)
            return false;

        var next = new List<(TimeSpan, TimeSpan)>();
        var changed = false;
        foreach (var (a, b) in _kept)
        {
            if (b <= from || a >= to)
            {
                next.Add((a, b));
                continue;
            }

            changed = true;
            if (a < from)
                next.Add((a, from));
            if (b > to)
                next.Add((to, b));
        }

        if (!changed)
            return false;

        _kept.Clear();
        _kept.AddRange(next.Where(s => s.Item2 - s.Item1 >= TimeSpan.FromMilliseconds(80)));
        SelectedIndex = -1;
        return true;
    }

    public int IndexAt(TimeSpan playhead)
    {
        for (var i = 0; i < _kept.Count; i++)
        {
            var (a, b) = _kept[i];
            if (playhead >= a && playhead <= b)
                return i;
        }

        return -1;
    }

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
    public TimeSpan SourceToPacked(TimeSpan sourceTime, bool? fillGaps = null)
    {
        foreach (var s in BuildPackedTimeline(fillGaps ?? FillGapsWithBlack))
        {
            if (s.IsBlack)
                continue;
            if (sourceTime >= s.SourceIn && sourceTime <= s.SourceOut)
                return s.TimelineIn + (sourceTime - s.SourceIn);
        }

        return sourceTime;
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
                $"{(i == SelectedIndex ? "▸" : "")}[{i + 1}] {Format(s.Start)}–{Format(s.End)}"));

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
    /// Optional black fillers for holes in source time between consecutive kept ranges.
    /// </summary>
    public IReadOnlyList<TimelineSegment> BuildPackedTimeline(bool fillSourceGapsWithBlack = true)
    {
        var list = new List<TimelineSegment>();
        var cursor = TimeSpan.Zero;
        for (var i = 0; i < _kept.Count; i++)
        {
            var (a, b) = _kept[i];
            if (fillSourceGapsWithBlack && i > 0)
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

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}

public readonly record struct TimelineSegment(
    int ClipIndex,
    TimeSpan TimelineIn,
    TimeSpan TimelineOut,
    TimeSpan SourceIn,
    TimeSpan SourceOut,
    bool IsBlack);
