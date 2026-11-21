namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>
/// The extra arguments a headless snapshot may carry to show the image panel in a real state:
/// <c>--flow-files "a.jpg|b.jpg"</c>, <c>--flow-opt key=value</c> (repeatable), <c>--flow-select N</c>,
/// <c>--flow-run</c> (run the tool over the files for real) and <c>--flow-out DIR</c>.
/// Read from the process command line so the shell's own argument handling does not change.
/// </summary>
internal sealed record FlowSnapshot(
    IReadOnlyList<string> Files,
    IReadOnlyDictionary<string, string> Options,
    int Select,
    bool Run,
    string? OutDir)
{
    public static FlowSnapshot? Current { get; } = Parse(Environment.GetCommandLineArgs());

    public static FlowSnapshot? Parse(string[] args)
    {
        if (!args.Any(a => string.Equals(a, "--snapshot", StringComparison.OrdinalIgnoreCase)))
            return null;
        string? filesArg = null, outDir = null;
        var select = 0;
        var run = false;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            var next = i + 1 < args.Length ? args[i + 1].Trim().Trim('"') : null;
            switch (a.ToLowerInvariant())
            {
                case "--flow-files": filesArg = next; i++; break;
                case "--flow-out": outDir = next; i++; break;
                case "--flow-select": _ = int.TryParse(next, out select); i++; break;
                case "--flow-run": run = true; break;
                case "--flow-opt":
                    if (next is not null && next.IndexOf('=') is var eq and > 0)
                        options[next[..eq]] = next[(eq + 1)..];
                    i++;
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(filesArg))
            return null;
        var files = filesArg.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return files.Count == 0 ? null : new FlowSnapshot(files, options, Math.Max(0, select), run, outDir);
    }
}
