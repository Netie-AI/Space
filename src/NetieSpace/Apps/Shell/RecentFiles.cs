using System.Text.Json;

namespace NetieSpace.Apps.Shell;

/// <summary>One row of recent.json.</summary>
public sealed record RecentEntry(string Path, DateTime OpenedAtUtc)
{
    public string Name => IoPath.GetFileName(Path);
    public string Folder => IoPath.GetDirectoryName(Path) ?? "";
}

/// <summary>
/// Per-app recent files (%LOCALAPPDATA%\NetieSpace\apps\&lt;app&gt;\recent.json). Pure IO, no WPF:
/// newest first, one row per path, files that no longer exist dropped on load, capped.
/// A missing or damaged file reads as empty and is never thrown to the UI.
/// </summary>
public sealed class RecentFiles
{
    public const int DefaultCap = 20;

    private readonly string _jsonPath;
    private readonly int _cap;
    private readonly Func<DateTime> _clock;

    public RecentFiles(string jsonPath, int cap = DefaultCap, Func<DateTime>? clock = null)
    {
        _jsonPath = jsonPath;
        _cap = Math.Max(1, cap);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public static string DefaultPath(AppIdentity app) =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", app.Key, "recent.json");

    public string JsonPath => _jsonPath;

    public IReadOnlyList<RecentEntry> Load()
    {
        var rows = new List<RecentEntry>();
        try
        {
            if (!IoFile.Exists(_jsonPath))
                return rows;
            var parsed = JsonSerializer.Deserialize<List<Row>>(IoFile.ReadAllText(_jsonPath));
            if (parsed is null)
                return rows;
            foreach (var r in parsed)
            {
                if (string.IsNullOrWhiteSpace(r.Path))
                    continue;
                rows.Add(new RecentEntry(r.Path, r.OpenedAtUtc));
            }
        }
        catch (Exception)
        {
            return new List<RecentEntry>();
        }

        return Normalize(rows.Where(r => IoFile.Exists(r.Path)));
    }

    /// <summary>Puts <paramref name="path"/> first (now), drops its older row, trims to the cap, saves.</summary>
    public IReadOnlyList<RecentEntry> Add(string path)
    {
        var rows = Load().ToList();
        rows.Add(new RecentEntry(path, _clock()));
        var result = Normalize(rows);
        Save(result);
        return result;
    }

    private IReadOnlyList<RecentEntry> Normalize(IEnumerable<RecentEntry> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<RecentEntry>();
        foreach (var r in rows.OrderByDescending(r => r.OpenedAtUtc))
        {
            if (seen.Add(r.Path))
                list.Add(r);
            if (list.Count >= _cap)
                break;
        }
        return list;
    }

    private void Save(IReadOnlyList<RecentEntry> rows)
    {
        try
        {
            var dir = IoPath.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrEmpty(dir))
                IoDirectory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(rows.Select(r => new Row { Path = r.Path, OpenedAtUtc = r.OpenedAtUtc }).ToList());
            var tmp = _jsonPath + ".tmp";
            IoFile.WriteAllText(tmp, json);
            IoFile.Move(tmp, _jsonPath, overwrite: true);
        }
        catch (Exception)
        {
            // Recent files are a convenience: a locked or read-only folder must not fail a run.
        }
    }

    private sealed class Row
    {
        public string Path { get; set; } = "";
        public DateTime OpenedAtUtc { get; set; }
    }
}
