using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// One page of the page grid. <see cref="File"/> is the 1-based position among the input files (0 = a blank page
/// to insert), <see cref="Page"/> the 1-based page in that file, <see cref="Rotation"/> the degrees to ADD to the
/// page's own rotation (0, 90, 180 or 270).
/// </summary>
public readonly record struct PlanPage(int File, int Page, int Rotation)
{
    public bool IsBlankInsert => File == 0;
    public static PlanPage Blank { get; } = new(0, 0, 0);
}

/// <summary>
/// What the page grid hands to the engines: the <c>pdf-organize</c> "plan" text, or the page list of
/// <c>pdf-remove-pages</c>. Pure, so the grid and the engine are proven to agree without a window.
/// </summary>
public static class PdfPagePlan
{
    public static int NormalizeRotation(int degrees) => (((degrees % 360) + 360) % 360);

    /// <summary>
    /// "1:1-3;2:1@90;blank": consecutive pages of one file with one rotation fold into one range, so a
    /// 400-page file that only lost page 7 is two items, not 399.
    /// </summary>
    public static string ToPlanString(IReadOnlyList<PlanPage> pages)
    {
        var items = new List<string>();
        var i = 0;
        while (i < pages.Count)
        {
            var p = pages[i];
            if (p.IsBlankInsert)
            {
                items.Add("blank");
                i++;
                continue;
            }
            var rot = NormalizeRotation(p.Rotation);
            var j = i;
            while (j + 1 < pages.Count
                   && !pages[j + 1].IsBlankInsert
                   && pages[j + 1].File == p.File
                   && pages[j + 1].Page == pages[j].Page + 1
                   && NormalizeRotation(pages[j + 1].Rotation) == rot)
                j++;
            var range = j == i
                ? p.Page.ToString(CultureInfo.InvariantCulture)
                : p.Page.ToString(CultureInfo.InvariantCulture) + "-" + pages[j].Page.ToString(CultureInfo.InvariantCulture);
            items.Add(p.File.ToString(CultureInfo.InvariantCulture) + ":" + range + (rot == 0 ? "" : "@" + rot.ToString(CultureInfo.InvariantCulture)));
            i = j + 1;
        }
        return string.Join(";", items);
    }

    /// <summary>"2,5-7": the pages of 1..<paramref name="total"/> that are not in <paramref name="kept"/>; empty when none is missing.</summary>
    public static string RemoveSpec(int total, IEnumerable<int> kept)
    {
        var keep = new HashSet<int>(kept);
        var sb = new StringBuilder();
        var p = 1;
        while (p <= total)
        {
            if (keep.Contains(p)) { p++; continue; }
            var q = p;
            while (q + 1 <= total && !keep.Contains(q + 1)) q++;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(p.ToString(CultureInfo.InvariantCulture));
            if (q > p) sb.Append('-').Append(q.ToString(CultureInfo.InvariantCulture));
            p = q + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Moves the items at <paramref name="selected"/> as one block to sit before the item that was at
    /// <paramref name="insertBefore"/> (0..Count; Count = the end). Order inside the block is kept.
    /// Returns the index of the block's first item afterwards.
    /// </summary>
    public static int MoveBlock<T>(List<T> list, IReadOnlyCollection<int> selected, int insertBefore)
    {
        var sel = selected.Where(i => i >= 0 && i < list.Count).Distinct().OrderBy(i => i).ToList();
        if (sel.Count == 0) return 0;
        insertBefore = Math.Clamp(insertBefore, 0, list.Count);
        var block = sel.Select(i => list[i]).ToList();
        var at = Enumerable.Range(0, insertBefore).Count(i => !sel.Contains(i));
        for (var k = sel.Count - 1; k >= 0; k--) list.RemoveAt(sel[k]);
        list.InsertRange(at, block);
        return at;
    }
}
