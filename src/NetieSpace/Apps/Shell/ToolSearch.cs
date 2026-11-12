namespace NetieSpace.Apps.Shell;

/// <summary>Case-insensitive token-AND match over name, verb, category and accepted file types.</summary>
public static class ToolSearch
{
    public const string NoToolsLine = "No tools";

    public static string[] Tokens(string? query) =>
        (query ?? "")
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

    public static bool Matches(HomeTile tile, string? query)
    {
        var tokens = Tokens(query);
        if (tokens.Length == 0)
            return true;
        var hay = string.Join(
            "\n",
            new[] { tile.Name, tile.Verb, tile.Category }
                .Concat(tile.Types ?? Array.Empty<string>()))
            .ToLowerInvariant();
        return tokens.All(t => hay.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>Categories reduced to the matching tiles; a category with none left is dropped.</summary>
    public static IReadOnlyList<HomeCategory> Filter(IReadOnlyList<HomeCategory> categories, string? query)
    {
        if (Tokens(query).Length == 0)
            return categories;
        return categories
            .Select(c => new HomeCategory(c.Name, c.Tiles.Where(t => Matches(t, query)).ToList()))
            .Where(c => c.Tiles.Count > 0)
            .ToList();
    }
}

/// <summary>What the home grid shows, as data: an optional open-with group, categories, or a plain line.</summary>
public sealed record HomeGrid(
    string? OpenWithTitle,
    IReadOnlyList<HomeTile> OpenWith,
    IReadOnlyList<HomeCategory> Categories,
    string? EmptyLine)
{
    public const string OpenWithHeading = "Open with";

    /// <summary>
    /// A typed query searches every category (the sidebar choice is ignored while searching).
    /// With <paramref name="openPath"/> set, the tools that accept the file come first.
    /// </summary>
    public static HomeGrid Build(
        IReadOnlyList<HomeCategory> categories, string? category, string? query, string? openPath)
    {
        var searching = ToolSearch.Tokens(query).Length > 0;
        var scope = searching || category is null
            ? categories
            : categories.Where(c => c.Name == category).ToList();

        var openWith = openPath is null
            ? new List<HomeTile>()
            : OpenWithRanking.Rank(categories.SelectMany(c => c.Tiles), openPath)
                .Where(t => ToolSearch.Matches(t, query))
                .ToList();

        var shown = ToolSearch.Filter(scope, query);
        var empty = shown.Count == 0 && openWith.Count == 0 ? ToolSearch.NoToolsLine : null;
        return new HomeGrid(openWith.Count > 0 ? OpenWithHeading : null, openWith, shown, empty);
    }
}
