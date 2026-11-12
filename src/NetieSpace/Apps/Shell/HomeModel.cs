using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>A tile on the home grid. <see cref="ReasonLine"/> is the registry's text when unavailable.</summary>
public sealed record HomeTile(
    string ToolId, string Glyph, string Name, string Verb, bool Available, string? ReasonLine, string Category);

public sealed record HomeCategory(string Name, IReadOnlyList<HomeTile> Tiles);

/// <summary>Another app of the family. <see cref="Arguments"/> is the exact argv to start it.</summary>
public sealed record FamilyTarget(AppIdentity App, string Label, string Arguments);

/// <summary>Everything the home view decides, as data. No WPF.</summary>
public static class HomeModel
{
    public static ToolApp ToolAppOf(AppIdentity app) => app.Key switch
    {
        "pdf" => ToolApp.Pdf,
        "image" => ToolApp.Image,
        "video" => ToolApp.Video,
        _ => throw new ArgumentOutOfRangeException(nameof(app), app.Key, "unknown app"),
    };

    /// <summary>Categories of this app in catalog order (first appearance), every entry present.</summary>
    public static IReadOnlyList<HomeCategory> Categories(AppIdentity app, IEnumerable<RegisteredTool> tools)
    {
        var toolApp = ToolAppOf(app);
        var order = new List<string>();
        var byCategory = new Dictionary<string, List<HomeTile>>(StringComparer.Ordinal);
        foreach (var t in tools)
        {
            var e = t.Entry;
            if (e.App != toolApp)
                continue;
            if (!byCategory.TryGetValue(e.Category, out var list))
            {
                byCategory[e.Category] = list = new List<HomeTile>();
                order.Add(e.Category);
            }
            list.Add(Tile(t));
        }
        return order.Select(c => new HomeCategory(c, byCategory[c])).ToList();
    }

    public static HomeTile Tile(RegisteredTool t) => new(
        t.Entry.Id,
        Monogram(t.Entry.Name),
        t.Entry.Name,
        t.Entry.Verb,
        t.Availability.Available,
        t.Availability.Available ? null : t.Availability.Text,
        t.Entry.Category);

    /// <summary>The catalog glyph is a slug, so the tile draws the first letter of the name.</summary>
    public static string Monogram(string name)
    {
        foreach (var ch in name)
            if (char.IsLetterOrDigit(ch))
                return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    /// <summary>The two other apps, in family order.</summary>
    public static IReadOnlyList<FamilyTarget> FamilyTargets(AppIdentity current) =>
        AppIdentity.All
            .Where(a => !string.Equals(a.Key, current.Key, StringComparison.OrdinalIgnoreCase))
            .Select(a => new FamilyTarget(a, a.DisplayName, "--app " + a.Key))
            .ToList();
}
