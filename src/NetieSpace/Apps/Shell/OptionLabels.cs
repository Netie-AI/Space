using System.Globalization;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// What a person reads for a stored option value. The value itself (what the runner and the
/// memory file see) never changes; only the text beside it does. Pure data.
/// </summary>
public static class OptionLabels
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["direction|right"] = "Rotate right 90 degrees",
        ["direction|left"] = "Rotate left 90 degrees",
        ["direction|180"] = "Rotate 180 degrees",
        ["angle|90"] = "90 degrees",
        ["angle|180"] = "180 degrees",
        ["angle|270"] = "270 degrees",
        ["filter|all"] = "All pages",
        ["filter|portrait"] = "Portrait pages only",
        ["filter|landscape"] = "Landscape pages only",
        ["audio|keep"] = "Keep the sound",
        ["audio|mute"] = "Remove the sound",
        ["axis|horizontal"] = "Flip left to right",
        ["axis|vertical"] = "Flip top to bottom",
        ["fit|pad"] = "Fit inside, add bars",
        ["fit|inside"] = "Fit inside the size",
        ["fit|cover"] = "Fill the size, crop the edges",
        ["aspect|free"] = "Free shape",
        ["aspect|1:1"] = "Square (1:1)",
        ["aspect|4:5"] = "Portrait (4:5)",
        ["aspect|16:9"] = "Wide (16:9)",
        ["fit|crop"] = "Fill the frame, crop the edges",
        ["mode|pad"] = "Fit inside, add bars",
        ["mode|crop"] = "Fill the frame, crop the edges",
        ["mode|fit"] = "Fit inside",
        ["mode|stretch"] = "Stretch to size",
        ["mode|extract"] = "Keep these pages",
        ["mode|delete"] = "Delete these pages",
        ["mode|pixels"] = "Pixels",
        ["mode|percentage"] = "Percent",
        ["mode|text"] = "Text",
        ["mode|image"] = "Picture",
        ["mode|name"] = "Typed name",
        ["mode|replace"] = "Replace the sound",
        ["mode|mix"] = "Mix with the sound",
        ["mode|blur"] = "Blur",
        ["mode|pixelate"] = "Pixelate",
        ["format|n"] = "1, 2, 3",
        ["format|page-n"] = "Page 1, Page 2",
        ["format|n-of-total"] = "1 of 10",
        ["height|original"] = "Original size",
        ["layer|above"] = "On top of the page",
        ["layer|below"] = "Behind the page",
        ["level|extreme"] = "Smallest file",
        ["level|recommended"] = "Recommended",
        ["level|low"] = "Best quality",
        ["margin|none"] = "No margin",
        ["pagesize|fit"] = "Fit to the picture",
        ["pagesize|a4"] = "A4",
        ["size|a4"] = "A4",
        ["size|letter"] = "Letter",
        ["size|legal"] = "Legal",
        ["split_mode|ranges"] = "Custom ranges",
        ["split_mode|fixed_range"] = "Every few pages",
        ["split_mode|remove_pages"] = "Without chosen pages",
        ["split_mode|filesize"] = "By largest file size",
    };

    private static readonly HashSet<string> FileTypeKeys = new(StringComparer.OrdinalIgnoreCase) { "format", "to", "image" };

    /// <summary>Readable text for <paramref name="value"/> of option <paramref name="key"/>.</summary>
    public static string For(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (Exact.TryGetValue(key + "|" + value, out var label))
            return label;
        if (FileTypeKeys.Contains(key))
            return value.ToUpperInvariant();
        if (key.Equals("height", StringComparison.OrdinalIgnoreCase) && value.All(char.IsDigit))
            return value + " pixels tall";
        return Humanize(value);
    }

    /// <summary>"bottom-center" becomes "Bottom center"; digits and one-word values keep their spelling.</summary>
    public static string Humanize(string value)
    {
        var spaced = value.Replace('-', ' ').Replace('_', ' ').Trim();
        return spaced.Length == 0
            ? value
            : char.ToUpper(spaced[0], CultureInfo.InvariantCulture) + spaced[1..];
    }

    /// <summary>
    /// A page-range box whose default is the "everything" spelling shows an empty box with this hint, so the
    /// person reads "All pages" and the runner still receives its own default. Null for any other default.
    /// </summary>
    public static string? PageRangeHint(string defaultValue) => defaultValue.Trim().ToLowerInvariant() switch
    {
        "1-end" => "All pages",
        "end" => "Last page only",
        _ => null,
    };
}
