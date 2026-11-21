using System.Text.RegularExpressions;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Tile icons: simple line drawings on a 24 x 24 grid, one WPF Path geometry string per catalog
/// category. Original drawings, pure data. The view strokes them (no fill) with the ink brush.
/// </summary>
public static class TileGlyphs
{
    public const double Grid = 24;

    public const string Fallback = "M4 7 H20 V19 H4 Z M9 7 V4 H15 V7 M4 12 H20";

    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Netie PDF
        ["Organize PDF"] = "M4 4 H20 V9 H4 Z M4 12 H20 V17 H4 Z M4 20 H13",
        ["Optimize PDF"] = "M12 3 V15 M7 10 L12 15 L17 10 M5 20 H19",
        ["Convert to PDF"] = "M5 3 H14 L19 8 V21 H5 Z M14 3 V8 H19 M15 14 H9 M11 12 L9 14 L11 16",
        ["Convert from PDF"] = "M5 3 H14 L19 8 V21 H5 Z M14 3 V8 H19 M9 14 H15 M13 12 L15 14 L13 16",
        ["Edit PDF"] = "M4 20 L5 15 L16 4 L20 8 L9 19 Z M14 6 L18 10",
        ["PDF Security"] = "M6 11 H18 V21 H6 Z M8 11 V8 C8 3 16 3 16 8 V11",
        ["PDF Intelligence"] = "M12 3 L14 10 L21 12 L14 14 L12 21 L10 14 L3 12 L10 10 Z",
        ["More"] = "M4 11 H6 V13 H4 Z M11 11 H13 V13 H11 Z M18 11 H20 V13 H18 Z",

        // Netie Image
        ["Convert"] = "M4 8 H18 M14 4 L18 8 L14 12 M20 16 H6 M10 12 L6 16 L10 20",
        ["Create"] = "M12 4 V20 M4 12 H20",
        ["Modify"] = "M4 7 H20 M9 4 V10 M4 17 H20 M15 14 V20",
        ["Optimize"] = "M4 4 L10 10 M4 8 V4 H8 M20 20 L14 14 M20 16 V20 H16",
        ["Security"] = "M12 3 L20 6 V12 C20 17 16 20 12 21 C8 20 4 17 4 12 V6 Z",

        // Netie Video
        ["Audio"] = "M4 9 H8 L13 5 V19 L8 15 H4 Z M16 9 C18 11 18 13 16 15",
        ["Convert and optimise"] = "M19 12 C19 16 16 19 12 19 C8 19 5 16 5 12 C5 8 8 5 12 5 H17 M14 2 L17 5 L14 8",
        ["Cut and assemble"] = "M5 5 L19 19 M5 19 L19 5 M4 4 H8 V8 H4 Z M16 16 H20 V20 H16 Z",
        ["Effects and colour"] = "M12 3 C12 3 5 11 5 15 C5 19 8 21 12 21 C16 21 19 19 19 15 C19 11 12 3 12 3 Z",
        ["Overlays and text"] = "M4 5 H20 M12 5 V19 M8 19 H16",
        ["Recording"] = "M12 5 C16 5 19 8 19 12 C19 16 16 19 12 19 C8 19 5 16 5 12 C5 8 8 5 12 5 Z M12 9 C13.5 9 15 10.5 15 12 C15 13.5 13.5 15 12 15 C10.5 15 9 13.5 9 12 C9 10.5 10.5 9 12 9 Z",
        ["Speed and time"] = "M12 4 C16 4 20 8 20 12 C20 16 16 20 12 20 C8 20 4 16 4 12 C4 8 8 4 12 4 Z M12 8 V12 L15 14",
        ["Subtitles and speech"] = "M4 5 H20 V16 H11 L6 20 V16 H4 Z M8 9 H16 M8 12 H13",
        ["Transform"] = "M7 3 V17 H21 M3 7 H17 V21",
    };

    public static IReadOnlyCollection<string> Categories => Table.Keys;

    /// <summary>The geometry for a category; the fallback box for an unknown or blank one.</summary>
    public static string ForCategory(string? category) =>
        category is not null && Table.TryGetValue(category.Trim(), out var d) ? d : Fallback;

    private static readonly Regex Token = new(@"[A-Za-z]|-?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

    private static readonly Dictionary<char, int> Arity = new()
    {
        ['M'] = 2, ['L'] = 2, ['H'] = 1, ['V'] = 1, ['C'] = 6, ['Q'] = 4, ['Z'] = 0,
    };

    /// <summary>
    /// True when the text is only path commands (M L H V C Q Z, either case), separators and
    /// numbers, every command has a whole number of argument groups, and it starts with M.
    /// </summary>
    public static bool IsValidPath(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return false;
        // Everything must be consumed by the tokenizer apart from spaces and commas.
        var rest = Token.Replace(data, "");
        if (rest.Any(ch => ch != ' ' && ch != ','))
            return false;

        var tokens = Token.Matches(data).Select(m => m.Value).ToList();
        if (tokens.Count == 0 || !char.IsLetter(tokens[0][0]) || char.ToUpperInvariant(tokens[0][0]) != 'M')
            return false;

        var i = 0;
        while (i < tokens.Count)
        {
            if (!char.IsLetter(tokens[i][0]))
                return false;
            if (!Arity.TryGetValue(char.ToUpperInvariant(tokens[i][0]), out var n))
                return false;
            i++;
            var count = 0;
            while (i < tokens.Count && !char.IsLetter(tokens[i][0]))
            {
                count++;
                i++;
            }
            if (n == 0 ? count != 0 : count == 0 || count % n != 0)
                return false;
        }
        return true;
    }
}
