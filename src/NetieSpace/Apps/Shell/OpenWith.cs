namespace NetieSpace.Apps.Shell;

/// <summary>Which tools can take a file, best first. Pure.</summary>
public static class OpenWithRanking
{
    /// <summary>
    /// Available tiles whose accepted types include the file's extension. A tool that names
    /// fewer types is more specific and ranks first; ties keep catalog order. A tool that
    /// accepts any file is listed last. Unavailable tools are left to the normal grid.
    /// </summary>
    public static IReadOnlyList<HomeTile> Rank(IEnumerable<HomeTile> tiles, string? path)
    {
        var ext = Extension(path);
        if (ext.Length == 0)
            return Array.Empty<HomeTile>();

        var scored = new List<(HomeTile Tile, int Score, int Order)>();
        var order = 0;
        foreach (var t in tiles)
        {
            order++;
            if (!t.Available)
                continue;
            var types = t.Types ?? Array.Empty<string>();
            if (types.Count == 0)
                scored.Add((t, int.MaxValue, order));
            else if (types.Contains(ext, StringComparer.OrdinalIgnoreCase))
                scored.Add((t, types.Count, order));
        }
        return scored.OrderBy(s => s.Score).ThenBy(s => s.Order).Select(s => s.Tile).ToList();
    }

    public static string Extension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        var ext = IoPath.GetExtension(path.Trim().Trim('"'));
        return ext.Length <= 1 ? "" : ext.ToLowerInvariant();
    }
}
