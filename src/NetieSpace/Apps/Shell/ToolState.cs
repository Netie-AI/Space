using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>What a tool page starts from: last option values and last output folder.</summary>
public sealed record ToolMemory(IReadOnlyDictionary<string, string> Options, string? OutDir)
{
    public static readonly ToolMemory Empty = new(new Dictionary<string, string>(), null);
}

/// <summary>
/// Per-tool memory (%LOCALAPPDATA%\NetieSpace\apps\&lt;app&gt;\tool-state.json). Pure IO, no WPF.
/// A missing, corrupt or stale file reads as empty. On read every remembered value is checked
/// against the entry as it is now: unknown keys, values no longer valid, a file option whose
/// file is gone and an output folder that no longer exists are dropped. Secrets are never kept.
/// </summary>
public sealed class ToolStateStore
{
    private const int MaxText = 500;
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex PageRangeText = new("^[0-9A-Za-z,\\- ]{1,200}$", RegexOptions.CultureInvariant);

    private readonly string _jsonPath;

    public ToolStateStore(string jsonPath) => _jsonPath = jsonPath;

    public string JsonPath => _jsonPath;

    public static string DefaultPath(AppIdentity app) =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", app.Key, "tool-state.json");

    /// <summary>An option whose value must not be written to disk.</summary>
    public static bool IsSecret(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    public ToolMemory Load(ToolEntry entry)
    {
        try
        {
            using var doc = ReadDocument();
            if (doc is null || !doc.RootElement.TryGetProperty(entry.Id, out var row) || row.ValueKind != JsonValueKind.Object)
                return ToolMemory.Empty;

            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (row.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
            {
                foreach (var o in entry.Options)
                {
                    if (IsSecret(o.Key) || !opts.TryGetProperty(o.Key, out var v) || v.ValueKind != JsonValueKind.String)
                        continue;
                    var clean = Sanitize(o, v.GetString());
                    if (clean is not null)
                        options[o.Key] = clean;
                }
            }

            string? outDir = null;
            if (row.TryGetProperty("outDir", out var od) && od.ValueKind == JsonValueKind.String)
            {
                var dir = od.GetString();
                if (!string.IsNullOrWhiteSpace(dir) && IoDirectory.Exists(dir))
                    outDir = dir;
            }
            return new ToolMemory(options, outDir);
        }
        catch (Exception)
        {
            return ToolMemory.Empty;
        }
    }

    /// <summary>
    /// Stores the values that are valid for <paramref name="entry"/>, keeping the other tools' rows.
    /// Returns false when the file could not be written; a failure never reaches the page.
    /// </summary>
    public bool Save(ToolEntry entry, IReadOnlyDictionary<string, string> values, string? outDir)
    {
        try
        {
            var rows = ReadRows();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var o in entry.Options)
            {
                if (IsSecret(o.Key) || !values.TryGetValue(o.Key, out var raw))
                    continue;
                var clean = Sanitize(o, raw);
                if (clean is not null)
                    options[o.Key] = clean;
            }
            rows[entry.Id] = new Dictionary<string, object?>
            {
                ["options"] = options,
                ["outDir"] = string.IsNullOrWhiteSpace(outDir) ? null : outDir,
            };

            var dir = IoPath.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrEmpty(dir))
                IoDirectory.CreateDirectory(dir);
            var tmp = _jsonPath + ".tmp";
            IoFile.WriteAllText(tmp, JsonSerializer.Serialize(rows));
            IoFile.Move(tmp, _jsonPath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The value as the entry would accept it (choices in their catalog spelling), or null to drop it.</summary>
    public static string? Sanitize(ToolOption o, string? raw)
    {
        if (raw is null)
            return null;
        var v = raw.Trim();
        var inv = CultureInfo.InvariantCulture;
        switch (o.Kind)
        {
            case ToolOptionKind.Choice:
            case ToolOptionKind.Grid:
            {
                var choices = o.Choices is { Count: > 0 } ? o.Choices : o.Kind == ToolOptionKind.Grid ? ToolPageModel.GridChoices : Array.Empty<string>();
                return choices.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase));
            }
            case ToolOptionKind.Int:
            case ToolOptionKind.Percent:
            {
                if (!int.TryParse(v, NumberStyles.AllowLeadingSign, inv, out var n))
                    return null;
                var lo = o.Min ?? (o.Kind == ToolOptionKind.Percent ? 0 : int.MinValue);
                var hi = o.Max ?? (o.Kind == ToolOptionKind.Percent ? 100 : int.MaxValue);
                return n < lo || n > hi ? null : n.ToString(inv);
            }
            case ToolOptionKind.Bool:
                return v is "true" or "false" ? v : null;
            case ToolOptionKind.Color:
                return HexColor.IsMatch(v) ? v : null;
            case ToolOptionKind.PageRange:
                return PageRangeText.IsMatch(v) ? v : null;
            case ToolOptionKind.File:
                return v.Length > 0 && v.Length <= 1000 && IoFile.Exists(v) ? v : null;
            default:
                return v.Length > 0 && v.Length <= MaxText && !v.Any(char.IsControl) ? v : null;
        }
    }

    private JsonDocument? ReadDocument()
    {
        if (!IoFile.Exists(_jsonPath))
            return null;
        var doc = JsonDocument.Parse(IoFile.ReadAllText(_jsonPath));
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
            return doc;
        doc.Dispose();
        return null;
    }

    /// <summary>Existing rows as plain data so a save keeps other tools' memory; damage reads as empty.</summary>
    private Dictionary<string, object?> ReadRows()
    {
        var rows = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            using var doc = ReadDocument();
            if (doc is null)
                return rows;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object)
                    rows[p.Name] = p.Value.Clone();
        }
        catch (Exception)
        {
            rows.Clear();
        }
        return rows;
    }
}
