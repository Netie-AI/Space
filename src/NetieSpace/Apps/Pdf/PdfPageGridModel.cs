using System;
using System.Collections.Generic;
using System.Linq;

namespace NetieSpace.Apps.Pdf;

/// <summary>What a page grid is allowed to do: Organize all of it, Remove only deletes, Rotate only rotates.</summary>
public readonly record struct PageGridRules(bool CanReorder, bool CanDelete, bool CanRotate, bool CanInsertBlank)
{
    public static PageGridRules Organize { get; } = new(true, true, true, true);
    public static PageGridRules Remove { get; } = new(false, true, false, false);
    public static PageGridRules Rotate { get; } = new(false, false, true, false);
}

/// <summary>
/// The page grid as data: the pages in their current order, which are selected, rotate / delete / move /
/// insert-blank and an undo stack. No WPF. The view draws <see cref="Pages"/> and forwards clicks.
/// </summary>
public sealed class PageGridModel
{
    private sealed record Snapshot(List<PlanPage> Pages, HashSet<int> Selected, int Anchor);

    private readonly Stack<Snapshot> _undo = new();
    private List<PlanPage> _pages = new();
    private readonly HashSet<int> _selected = new();
    private int _anchor = -1;
    private readonly List<int> _fileLengths = new();

    public PageGridModel(PageGridRules rules) => Rules = rules;

    public PageGridRules Rules { get; }
    public IReadOnlyList<PlanPage> Pages => _pages;
    public IReadOnlyCollection<int> Selected => _selected;
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Pages each input file had when it was loaded, in file order (file n is index n-1).</summary>
    public IReadOnlyList<int> FileLengths => _fileLengths;

    public bool IsSelected(int index) => _selected.Contains(index);

    /// <summary>Replaces everything with the pages of these files, in file order; clears selection and undo.</summary>
    public void Load(IEnumerable<int> pageCounts)
    {
        _pages = new List<PlanPage>();
        _fileLengths.Clear();
        var file = 0;
        foreach (var count in pageCounts)
        {
            file++;
            _fileLengths.Add(Math.Max(0, count));
            for (var p = 1; p <= count; p++) _pages.Add(new PlanPage(file, p, 0));
        }
        _selected.Clear();
        _anchor = -1;
        _undo.Clear();
    }

    /// <summary>Adds the pages of one more input file at the end; edits already made stay. Not undoable (clear the files instead).</summary>
    public void AppendFile(int pageCount)
    {
        _fileLengths.Add(Math.Max(0, pageCount));
        var file = _fileLengths.Count;
        for (var p = 1; p <= pageCount; p++) _pages.Add(new PlanPage(file, p, 0));
    }

    /// <summary>Back to the loaded order: every page of every file, unrotated, no blanks.</summary>
    public void Reset()
    {
        var current = new Snapshot(_pages.ToList(), new HashSet<int>(_selected), _anchor);
        var counts = _fileLengths.ToList();
        var history = _undo.ToArray(); // newest first
        Load(counts);
        for (var i = history.Length - 1; i >= 0; i--) _undo.Push(history[i]);
        _undo.Push(current);
    }

    /// <summary>Click, ctrl+click (toggle) and shift+click (range from the anchor) as in Explorer.</summary>
    public void Click(int index, bool shift, bool ctrl)
    {
        if (index < 0 || index >= _pages.Count) return;
        if (shift && _anchor >= 0 && _anchor < _pages.Count)
        {
            var a = Math.Min(_anchor, index);
            var b = Math.Max(_anchor, index);
            if (!ctrl) _selected.Clear();
            for (var i = a; i <= b; i++) _selected.Add(i);
            return;
        }
        if (ctrl)
        {
            if (!_selected.Remove(index)) _selected.Add(index);
            _anchor = index;
            return;
        }
        _selected.Clear();
        _selected.Add(index);
        _anchor = index;
    }

    public void SelectAll()
    {
        _selected.Clear();
        for (var i = 0; i < _pages.Count; i++) _selected.Add(i);
    }

    public void ClearSelection()
    {
        _selected.Clear();
        _anchor = -1;
    }

    /// <summary>Rotates every selected page by <paramref name="delta"/> degrees (90, -90, 180). False when nothing happened.</summary>
    public bool RotateSelected(int delta)
    {
        if (!Rules.CanRotate || _selected.Count == 0) return false;
        Push();
        foreach (var i in _selected)
        {
            var p = _pages[i];
            if (p.IsBlankInsert) continue;
            _pages[i] = p with { Rotation = PdfPagePlan.NormalizeRotation(p.Rotation + delta) };
        }
        return true;
    }

    /// <summary>Removes the selected pages. Returns how many went.</summary>
    public int DeleteSelected()
    {
        if (!Rules.CanDelete || _selected.Count == 0) return 0;
        Push();
        var gone = _selected.OrderByDescending(i => i).ToList();
        foreach (var i in gone) _pages.RemoveAt(i);
        _selected.Clear();
        _anchor = -1;
        return gone.Count;
    }

    /// <summary>Inserts a blank page after the last selected page (at the end when nothing is selected).</summary>
    public bool InsertBlank()
    {
        if (!Rules.CanInsertBlank) return false;
        Push();
        var at = _selected.Count == 0 ? _pages.Count : _selected.Max() + 1;
        _pages.Insert(at, PlanPage.Blank);
        _selected.Clear();
        _selected.Add(at);
        _anchor = at;
        return true;
    }

    /// <summary>Drops the selected pages as a block before the page at <paramref name="insertBefore"/>.</summary>
    public bool MoveSelected(int insertBefore)
    {
        if (!Rules.CanReorder || _selected.Count == 0) return false;
        var before = _pages.ToList();
        var probe = _pages.ToList();
        var start = PdfPagePlan.MoveBlock(probe, _selected, insertBefore);
        if (probe.SequenceEqual(before)) return false;
        Push();
        _pages = probe;
        var n = _selected.Count;
        _selected.Clear();
        for (var i = 0; i < n; i++) _selected.Add(start + i);
        _anchor = start;
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var s = _undo.Pop();
        _pages = s.Pages;
        _selected.Clear();
        foreach (var i in s.Selected) _selected.Add(i);
        _anchor = s.Anchor;
        return true;
    }

    /// <summary>True when something differs from the loaded state: order, rotation, blanks or deleted pages.</summary>
    public bool IsChanged
    {
        get
        {
            var expected = new List<PlanPage>();
            for (var f = 0; f < _fileLengths.Count; f++)
                for (var p = 1; p <= _fileLengths[f]; p++) expected.Add(new PlanPage(f + 1, p, 0));
            return !expected.SequenceEqual(_pages);
        }
    }

    public string PlanString() => PdfPagePlan.ToPlanString(_pages);

    /// <summary>The pdf-remove-pages page list for file 1: the pages that are no longer in the grid.</summary>
    public string RemovedSpec() =>
        PdfPagePlan.RemoveSpec(_fileLengths.Count > 0 ? _fileLengths[0] : 0, _pages.Where(p => p.File == 1).Select(p => p.Page));

    private void Push() => _undo.Push(new Snapshot(_pages.ToList(), new HashSet<int>(_selected), _anchor));
}
