using System.IO;
using NetieSpace.Models;

namespace NetieSpace.Helpers;

/// <summary>
/// Quick Look flow decisions that need no WPF, so xunit pins them on Linux: which keys step to the
/// neighbouring file and which scroll the content, the folder order and "3 of 12", the page and find
/// readouts, the open-to-content budget, and the cheat sheet. PreviewWindow calls these; it does not
/// repeat the rules.
/// </summary>
public static class PeekFlowRules
{
    /// <summary>Warm open-to-content budget in milliseconds (founder: macOS Quick Look feel).</summary>
    public const int OpenBudgetMs = 600;

    /// <summary>Kinds whose content scrolls: Up/Down, Home/End and Page keys belong to the content.</summary>
    public static bool Scrolls(PreviewKind kind) =>
        kind is PreviewKind.Pdf or PreviewKind.Text or PreviewKind.Markdown or PreviewKind.Html or PreviewKind.Office;

    /// <summary>Left/Right always step to the neighbouring file. Up/Down only where nothing scrolls.</summary>
    public static bool ArrowStepsFile(PreviewKind kind, bool vertical) => !vertical || !Scrolls(kind);

    /// <summary>Home/End jump to the first/last file only where the content does not scroll.</summary>
    public static bool HomeEndStepsFile(PreviewKind kind) => !Scrolls(kind);

    /// <summary>Ctrl+F: words to look for exist in documents, not in pictures and clips.</summary>
    public static bool FindSupported(PreviewKind kind) => Scrolls(kind);

    // ---- folder order ----

    /// <summary>Hidden helper files that are never a "next file" (Explorer does not show them either).</summary>
    public static bool IsPeekableName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (fileName.StartsWith("~$", StringComparison.Ordinal))
            return false;
        return !fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
               && !fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Files of the folder of <paramref name="currentPath"/>, in Explorer's natural name order.
    /// Folders are skipped (EnumerateFiles), so are hidden and system files and Office lock files.
    /// An unreadable folder yields an empty list: the caller shows "End of folder", never throws.
    /// </summary>
    public static IReadOnlyList<string> SiblingFiles(string currentPath)
    {
        try
        {
            var dir = Directory.Exists(currentPath) ? currentPath : Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Array.Empty<string>();
            var files = new List<string>();
            // FileInfo carries the attributes from the directory read: no extra call per file in a big folder.
            foreach (var fi in new DirectoryInfo(dir).EnumerateFiles())
            {
                if (!IsPeekableName(fi.Name))
                    continue;
                try
                {
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;
                }
                catch { continue; }
                files.Add(fi.FullName);
            }
            files.Sort((a, b) => NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The neighbour of <paramref name="current"/> in <paramref name="files"/>, wrapping at both ends.
    /// Null when there is nothing else to show (a folder with one file).
    /// </summary>
    public static string? Step(IReadOnlyList<string> files, string current, int delta)
    {
        if (files.Count < 2 || delta == 0)
            return null;
        var i = IndexOf(files, current);
        if (i < 0)
            return delta > 0 ? files[0] : files[^1];
        var n = files.Count;
        return files[(((i + Math.Sign(delta)) % n) + n) % n];
    }

    public static string? Extreme(IReadOnlyList<string> files, bool toEnd) =>
        files.Count == 0 ? null : toEnd ? files[^1] : files[0];

    public static int IndexOf(IReadOnlyList<string> files, string current)
    {
        for (var i = 0; i < files.Count; i++)
            if (string.Equals(files[i], current, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>"3 of 12"; empty when the folder holds only this file or the file is not in the list.</summary>
    public static string CounterText(IReadOnlyList<string> files, string current)
    {
        var i = IndexOf(files, current);
        return i < 0 || files.Count < 2 ? "" : $"{i + 1} of {files.Count}";
    }

    /// <summary>Explorer's name order: case-insensitive, runs of digits compare as numbers (file2 before file10).</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (si < a.Length && a[si] == '0') si++;
                while (sj < b.Length && b[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < a.Length && char.IsDigit(a[ei])) ei++;
                while (ej < b.Length && char.IsDigit(b[ej])) ej++;
                var li = ei - si;
                var lj = ej - sj;
                if (li != lj)
                    return li < lj ? -1 : 1;
                var c = string.CompareOrdinal(a, si, b, sj, li);
                if (c != 0)
                    return c < 0 ? -1 : 1;
                // Equal numbers: consume the whole digit runs (including leading zeros) on both sides.
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                continue;
            }
            var ca = char.ToUpperInvariant(a[i]);
            var cb = char.ToUpperInvariant(b[j]);
            if (ca != cb)
                return ca < cb ? -1 : 1;
            i++;
            j++;
        }
        if (i < a.Length) return 1;
        if (j < b.Length) return -1;
        return string.CompareOrdinal(a, b);
    }

    // ---- readouts ----

    public static string PageText(int page, int pages) =>
        pages < 1 ? "" : $"Page {Math.Clamp(page, 1, pages)} / {pages}";

    /// <summary>Find readout: empty before a query, "No matches", or "2 of 9" (index is zero based).</summary>
    public static string FindText(string? query, int count, int index) =>
        string.IsNullOrWhiteSpace(query) ? ""
        : count <= 0 ? "No matches"
        : $"{Math.Clamp(index, 0, count - 1) + 1} of {count}";

    /// <summary>Step the current match with wrap, so Enter on the last match returns to the first.</summary>
    public static int NextMatch(int index, int count, int delta) =>
        count <= 0 ? -1 : (((index + (delta < 0 ? -1 : 1)) % count) + count) % count;

    // ---- timing ----

    /// <summary>One app.log line per open. The desktop check reads these; the format is the contract.</summary>
    public static string TimingLine(PreviewKind kind, long ms, bool warm) =>
        $"[peek] open-to-content kind={kind} ms={ms} warm={(warm ? 1 : 0)}";

    public static bool TryParseTimingLine(string line, out string kind, out long ms, out bool warm)
    {
        kind = "";
        ms = 0;
        warm = false;
        const string tag = "[peek] open-to-content kind=";
        var at = line.IndexOf(tag, StringComparison.Ordinal);
        if (at < 0)
            return false;
        var parts = line[(at + tag.Length)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[1].StartsWith("ms=") || !parts[2].StartsWith("warm="))
            return false;
        if (!long.TryParse(parts[1][3..], out ms))
            return false;
        kind = parts[0];
        warm = parts[2][5..] == "1";
        return true;
    }

    public static bool WithinBudget(long ms) => ms >= 0 && ms <= OpenBudgetMs;

    // ---- cheat sheet ----

    public static readonly IReadOnlyList<(string Keys, string What)> CheatSheet = new (string, string)[]
    {
        ("Left / Right", "Previous / next file in this folder"),
        ("Up / Down", "Scroll, or previous / next file for pictures and clips"),
        ("Page Up / Page Down", "Scroll one screen"),
        ("Home / End", "Top / bottom, or first / last file for pictures and clips"),
        ("Ctrl+F", "Find in this file (Enter next, Shift+Enter previous)"),
        ("Ctrl+C", "Copy the selected text"),
        ("Ctrl + / Ctrl -", "Zoom in / out (Ctrl+0 to fit)"),
        ("?", "Show or hide this list"),
        ("Esc", "Close the find bar, then the peek"),
    };
}
