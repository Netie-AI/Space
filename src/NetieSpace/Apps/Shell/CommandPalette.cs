namespace NetieSpace.Apps.Shell;

public enum PaletteKind { Tool, RecentFile, Command }

/// <summary>One row of the command palette. <see cref="Key"/> is a tool id, a file path or a command name.</summary>
public sealed record PaletteItem(PaletteKind Kind, string Key, string Title, string Subtitle, bool Enabled = true)
{
    public const string CommandOpenFile = "open-file";
    public const string CommandAllTools = "all-tools";
    public const string CommandHelp = "help";
}

/// <summary>
/// Ctrl+K / Ctrl+P: one search over tools, recent files and the few app commands. Pure. Typing 2 or 3 letters has to land
/// on the right tool, so a name that starts with the letters beats a word that starts with them, which beats the letters
/// appearing in order ("cmp" finds Compress), which beats a match in the verb, category or file types.
/// </summary>
public static class CommandPalette
{
    public const int MaxRows = 12;

    public static IReadOnlyList<PaletteItem> Build(
        IEnumerable<HomeTile> tiles,
        IEnumerable<RecentEntry> recentFiles,
        IEnumerable<string> recentToolIds,
        string? pendingFile)
    {
        var tileList = tiles.ToList();
        var byId = tileList.ToDictionary(t => t.ToolId, StringComparer.Ordinal);
        var items = new List<PaletteItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddTool(HomeTile t)
        {
            if (seen.Add(t.ToolId))
                items.Add(new PaletteItem(PaletteKind.Tool, t.ToolId, t.Name,
                    t.Available ? $"{t.Category} - {t.Verb}{TypesText(t)}" : $"{t.Category} - not available: {t.ReasonLine}{TypesText(t)}",
                    t.Available));
        }

        // Order is the empty-query order: tools that suit the open file, then the ones used lately, then the rest.
        foreach (var t in OpenWithRanking.Rank(tileList, pendingFile).Take(OpenWithRanking.SuggestionCount))
            AddTool(t);
        foreach (var id in recentToolIds)
            if (byId.TryGetValue(id, out var t))
                AddTool(t);
        foreach (var f in recentFiles)
            items.Add(new PaletteItem(PaletteKind.RecentFile, f.Path, f.Name, f.Folder));
        foreach (var t in tileList)
            AddTool(t);

        items.Add(new PaletteItem(PaletteKind.Command, PaletteItem.CommandOpenFile, "Open a file...", "Ctrl+O"));
        items.Add(new PaletteItem(PaletteKind.Command, PaletteItem.CommandAllTools, "All tools", "Alt+Left"));
        items.Add(new PaletteItem(PaletteKind.Command, PaletteItem.CommandHelp, "Keyboard help", "F1"));
        return items;
    }

    // The accepted file types stay in the searchable text, so "docx" finds the Word tool.
    private static string TypesText(HomeTile t) => t.Types is { Count: > 0 } ? " - " + string.Join(" ", t.Types) : "";

    /// <summary>Items that match every word typed, best first; an empty query returns the first rows in build order.</summary>
    public static IReadOnlyList<PaletteItem> Search(IReadOnlyList<PaletteItem> all, string? query, int max = MaxRows)
    {
        var tokens = ToolSearch.Tokens(query);
        if (tokens.Length == 0)
            return all.Take(max).ToList();

        var scored = new List<(PaletteItem Item, int Score, int Order)>();
        for (var i = 0; i < all.Count; i++)
        {
            var total = 0;
            foreach (var t in tokens)
            {
                var s = Score(all[i], t);
                if (s == 0) { total = 0; break; }
                total += s;
            }
            if (total > 0)
                scored.Add((all[i], total, i));
        }
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Order)
            .Take(max)
            .Select(s => s.Item)
            .ToList();
    }

    private static int Score(PaletteItem item, string token)
    {
        var title = item.Title.ToLowerInvariant();
        if (title.StartsWith(token, StringComparison.Ordinal))
            return 100;
        if (title.Split(' ', '-', '.', '_').Any(w => w.StartsWith(token, StringComparison.Ordinal)))
            return 80;
        if (title.Contains(token, StringComparison.Ordinal))
            return 60;
        if (token.Length >= 2 && InOrder(title, token))
            return 40;
        if (item.Subtitle.Contains(token, StringComparison.OrdinalIgnoreCase))
            return 20;
        return 0;
    }

    private static bool InOrder(string title, string letters)
    {
        var at = 0;
        foreach (var ch in title)
            if (at < letters.Length && ch == letters[at])
                at++;
        return at == letters.Length;
    }

    /// <summary>The arrow keys move through <paramref name="count"/> rows and wrap at both ends; -1 when there are none.</summary>
    public static int Move(int current, int delta, int count)
    {
        if (count <= 0)
            return -1;
        var next = (current + delta) % count;
        return next < 0 ? next + count : next;
    }
}
