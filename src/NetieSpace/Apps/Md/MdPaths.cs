using System.IO;
using System.Text;

namespace NetieSpace.Apps.Md;

/// <summary>
/// Path rules shared by pictures and links of a Markdown document. Pure: no WPF, no Markdig.
/// A reference is first decoded once, then split into segments; every segment must be an
/// ordinary file name (no drive, stream, device name or control character).
/// </summary>
public static class MdPaths
{
    private static readonly string[] Reserved =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>The scheme of <paramref name="reference"/> ("http", "file"...), or null for a relative reference.
    /// A single letter before ':' is a drive ("C:\a.png"), reported as "drive".</summary>
    public static string? SchemeOf(string reference)
    {
        var colon = reference.IndexOf(':');
        if (colon <= 0)
            return null;
        for (var i = 0; i < colon; i++)
        {
            var c = reference[i];
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (i > 0 && (char.IsDigit(c) || c == '+' || c == '-' || c == '.'));
            if (!ok)
                return null;
        }
        return colon == 1 ? "drive" : reference[..colon].ToLowerInvariant();
    }

    /// <summary>Cuts "?query" and "#fragment". The fragment is returned separately (still encoded).</summary>
    public static string SplitTail(string reference, out string fragment)
    {
        fragment = "";
        var hash = reference.IndexOf('#');
        if (hash >= 0)
        {
            fragment = reference[(hash + 1)..];
            reference = reference[..hash];
        }
        var q = reference.IndexOf('?');
        return q >= 0 ? reference[..q] : reference;
    }

    /// <summary>
    /// Decodes <paramref name="path"/> once and returns clean segments, with "." dropped and ".." folded.
    /// Null when the path is empty, absolute, climbs above the start (unless <paramref name="allowClimb"/>)
    /// or holds an unsafe segment. With allowClimb the leading ".." segments are kept, so the caller
    /// can combine them with the folder.
    /// </summary>
    public static List<string>? Segments(string path, bool allowClimb)
    {
        string decoded;
        try { decoded = Uri.UnescapeDataString(path); }
        catch (UriFormatException) { return null; }
        decoded = decoded.Replace('\\', '/');
        if (decoded.Length == 0 || decoded[0] == '/')
            return null; // absolute, or a UNC / protocol-relative path

        var result = new List<string>();
        foreach (var segment in decoded.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (result.Count > 0 && result[^1] != "..")
                    result.RemoveAt(result.Count - 1);
                else if (allowClimb)
                    result.Add("..");
                else
                    return null;
                continue;
            }
            if (!SafeName(segment))
                return null;
            result.Add(segment);
        }
        return result.Count == 0 ? null : result;
    }

    public static bool SafeName(string name)
    {
        if (name.Length == 0 || name.Length > 255)
            return false;
        foreach (var c in name)
            if (c < 0x20 || c == '<' || c == '>' || c == ':' || c == '"' || c == '|' || c == '?' || c == '*' || c == '\\' || c == '/')
                return false;
        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;
        var stem = name.Split('.')[0].Trim().ToUpperInvariant();
        return Array.IndexOf(Reserved, stem) < 0;
    }

    /// <summary>Escapes each segment for a URL path under the picture host.</summary>
    public static string UrlPath(IEnumerable<string> segments)
    {
        var sb = new StringBuilder();
        foreach (var s in segments)
        {
            if (sb.Length > 0)
                sb.Append('/');
            sb.Append(Uri.EscapeDataString(s));
        }
        return sb.ToString();
    }
}
