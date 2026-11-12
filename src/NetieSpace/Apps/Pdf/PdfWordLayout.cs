using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UglyToad.PdfPig.Content;

namespace NetieSpace.Apps.Pdf;

/// <summary>One visual line of text on a page, rebuilt from word boxes.</summary>
internal sealed class PdfTextLine
{
    public List<Word> Words { get; } = new();
    public double Baseline { get; set; }
    public double Size { get; set; }
    public double Left => Words.Min(w => w.BoundingBox.Left);
    public double Right => Words.Max(w => w.BoundingBox.Right);
    public double Top => Words.Max(w => w.BoundingBox.Top);
    public double Bottom => Words.Min(w => w.BoundingBox.Bottom);
    public string Text => string.Join(" ", Words.Select(w => PdfText.Clean(w.Text)).Where(t => t.Length > 0));
}

internal static class PdfText
{
    /// <summary>Drops characters XML 1.0 cannot carry (controls, lone surrogates, U+FFFE and U+FFFF).</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; continue; }
            if (char.IsSurrogate(c)) continue;
            if (c < 0x20 || c == 0xFFFE || c == 0xFFFF || (c >= 0x7F && c <= 0x9F)) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public static double WordSize(Word w)
    {
        var l = w.Letters;
        if (l.Count == 0) return Math.Max(1, w.BoundingBox.Height);
        var s = l.Max(x => x.PointSize);
        return s > 0 ? s : Math.Max(1, w.BoundingBox.Height);
    }

    public static double WordBaseline(Word w) =>
        w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom;

    /// <summary>Words to lines: same baseline within a third of the type size, left to right, top to bottom.</summary>
    public static List<PdfTextLine> Lines(IEnumerable<Word> words)
    {
        var ordered = words.Where(w => PdfText.Clean(w.Text).Length > 0)
            .OrderByDescending(WordBaseline).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<PdfTextLine>();
        PdfTextLine? cur = null;
        foreach (var w in ordered)
        {
            var b = WordBaseline(w);
            var size = WordSize(w);
            if (cur is null || Math.Abs(cur.Baseline - b) > 0.33 * Math.Max(cur.Size, size))
            {
                cur = new PdfTextLine { Baseline = b, Size = size };
                lines.Add(cur);
            }
            cur.Words.Add(w);
            cur.Size = Math.Max(cur.Size, size);
        }
        foreach (var l in lines) l.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }
}
