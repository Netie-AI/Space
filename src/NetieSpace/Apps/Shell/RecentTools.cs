using System.Text.Json;

namespace NetieSpace.Apps.Shell;

/// <summary>One row of recent-tools.json.</summary>
public sealed record RecentTool(string ToolId, DateTime UsedAtUtc);

/// <summary>
/// Per-app recent tools (%LOCALAPPDATA%\NetieSpace\apps\&lt;app&gt;\recent-tools.json). Same rules as <see cref="RecentFiles"/>:
/// newest first, one row per tool, a damaged file reads as empty, a failed save never reaches the UI. Ids the catalog no
/// longer has are dropped on load, so a removed tool cannot leave a dead row on the home page.
/// </summary>
public sealed class RecentTools
{
    public const int DefaultCap = 8;

    private readonly string _jsonPath;
    private readonly int _cap;
    private readonly Func<DateTime> _clock;

    public RecentTools(string jsonPath, int cap = DefaultCap, Func<DateTime>? clock = null)
    {
        _jsonPath = jsonPath;
        _cap = Math.Max(1, cap);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public static string DefaultPath(AppIdentity app) =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", app.Key, "recent-tools.json");

    public string JsonPath => _jsonPath;

    /// <param name="knownIds">When given, rows for any other id are left out.</param>
    public IReadOnlyList<RecentTool> Load(IReadOnlySet<string>? knownIds = null)
    {
        try
        {
            if (!IoFile.Exists(_jsonPath))
                return Array.Empty<RecentTool>();
            var parsed = JsonSerializer.Deserialize<List<Row>>(IoFile.ReadAllText(_jsonPath));
            if (parsed is null)
                return Array.Empty<RecentTool>();
            return Normalize(parsed
                .Where(r => !string.IsNullOrWhiteSpace(r.ToolId) && (knownIds is null || knownIds.Contains(r.ToolId)))
                .Select(r => new RecentTool(r.ToolId, r.UsedAtUtc)));
        }
        catch (Exception)
        {
            return Array.Empty<RecentTool>();
        }
    }

    public IReadOnlyList<RecentTool> Add(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            return Load();
        var rows = Load().ToList();
        rows.Add(new RecentTool(toolId, _clock()));
        var result = Normalize(rows);
        Save(result);
        return result;
    }

    private IReadOnlyList<RecentTool> Normalize(IEnumerable<RecentTool> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<RecentTool>();
        foreach (var r in rows.OrderByDescending(r => r.UsedAtUtc))
        {
            if (seen.Add(r.ToolId))
                list.Add(r);
            if (list.Count >= _cap)
                break;
        }
        return list;
    }

    private void Save(IReadOnlyList<RecentTool> rows)
    {
        try
        {
            var dir = IoPath.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrEmpty(dir))
                IoDirectory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(rows.Select(r => new Row { ToolId = r.ToolId, UsedAtUtc = r.UsedAtUtc }).ToList());
            var tmp = _jsonPath + ".tmp";
            IoFile.WriteAllText(tmp, json);
            IoFile.Move(tmp, _jsonPath, overwrite: true);
        }
        catch (Exception)
        {
            // A convenience list: a locked or read-only folder must not fail a run.
        }
    }

    private sealed class Row
    {
        public string ToolId { get; set; } = "";
        public DateTime UsedAtUtc { get; set; }
    }
}
