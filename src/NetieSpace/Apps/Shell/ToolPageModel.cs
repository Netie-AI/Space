using System.Globalization;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

public enum OptionControl { ComboBox, NumberBox, CheckBox, TextBox, FileBrowse, GridCombo }

public sealed record OptionDescriptor(
    string Key,
    OptionControl Control,
    string Label,
    string Default,
    IReadOnlyList<string> Choices,
    int? Min,
    int? Max,
    bool IsPageRange = false)
{
    /// <summary>What a person reads for a choice. The choice itself is what the runner receives.</summary>
    public string ChoiceLabel(string value) => OptionLabels.For(Key, value);

    /// <summary>Greyed text of an empty page-range box, when the default means "everything"; else null.</summary>
    public string? Hint => IsPageRange ? OptionLabels.PageRangeHint(Default) : null;

    /// <summary>The text the control starts with: empty when <see cref="Hint"/> stands in for the default.</summary>
    public string StartText => Hint is not null ? "" : Default;
}

/// <summary>The result card as data: what the person reads after a run.</summary>
public sealed record ResultSummary(bool Ok, string Title, string Detail, string Line, string? Primary, int FileCount);

public sealed record FilesCheck(bool Ok, string? Message);

/// <summary>
/// Every decision of the tool page, from a <see cref="RegisteredTool"/>. No WPF: the view only
/// instantiates controls from these descriptors and forwards clicks.
/// </summary>
public sealed class ToolPageModel
{
    public static readonly IReadOnlyList<string> GridChoices = new[]
    {
        "top-left", "top-center", "top-right",
        "middle-left", "middle-center", "middle-right",
        "bottom-left", "bottom-center", "bottom-right",
    };

    private readonly RegisteredTool _tool;

    public ToolPageModel(RegisteredTool tool)
    {
        _tool = tool;
        Options = tool.Entry.Options.Select(Describe).ToList();
    }

    public string ToolId => _tool.Entry.Id;
    public string Name => _tool.Entry.Name;
    /// <summary>The action button text: the entry's verb, verbatim.</summary>
    public string ActionLabel => _tool.Entry.Verb;
    public bool Available => _tool.Availability.Available && _tool.Runner is not null;
    public RegisteredTool Tool => _tool;
    public IReadOnlyList<OptionDescriptor> Options { get; }
    public bool AllowMultiple => _tool.Entry.MaxFiles > 1;
    public int MinFiles => _tool.Entry.MinFiles;
    public int MaxFiles => _tool.Entry.MaxFiles;

    /// <summary>The registry's reason, verbatim; null when the tool is available.</summary>
    public string? UnavailableLine => Available ? null : _tool.Availability.Text;

    /// <summary>Source and size of the confirm-to-install offer; only for an unavailable tool that has one.</summary>
    public string? InstallOfferLine =>
        Available || _tool.Entry.InstallOffer is not { } o
            ? null
            : $"{o.SourceUrl} ({FormatSize(o.SizeBytes)})";

    /// <summary>The action is live only for an available tool with a valid file list.</summary>
    public bool ActionEnabled(IReadOnlyList<string> files) => Available && ValidateFiles(files).Ok;

    /// <summary>Delegates to the runner's own validation so the page can never disagree with it.</summary>
    public FilesCheck ValidateFiles(IReadOnlyList<string> files)
    {
        var msg = ToolRunner.Validate(
            _tool.Entry,
            new ToolRequest(files, "out", new Dictionary<string, string>()),
            out _);
        return new FilesCheck(msg is null, msg);
    }

    /// <summary>OpenFileDialog filter: the accepted types, or every file.</summary>
    public string BrowseFilter()
    {
        var types = _tool.Entry.AcceptedTypes
            .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();
        return types.Count == 0
            ? "All files|*.*"
            : "Supported files|" + string.Join(";", types.Select(t => "*." + t));
    }

    /// <summary>True when the extension of <paramref name="path"/> is one the tool accepts.</summary>
    public bool Accepts(string path)
    {
        var accepted = _tool.Entry.AcceptedTypes;
        return accepted.Count == 0
            || accepted.Contains(IoPath.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds <paramref name="added"/> after <paramref name="current"/>, one row per path.</summary>
    public static IReadOnlyList<string> MergeFiles(IReadOnlyList<string> current, IEnumerable<string> added)
    {
        var list = current.ToList();
        foreach (var f in added)
            if (!string.IsNullOrWhiteSpace(f) && !list.Contains(f, StringComparer.OrdinalIgnoreCase))
                list.Add(f);
        return list;
    }

    /// <summary>Outputs go beside the first input.</summary>
    public static string DefaultOutDir(IReadOnlyList<string> files) =>
        files.Count > 0 ? IoPath.GetDirectoryName(IoPath.GetFullPath(files[0])) ?? "" : "";

    /// <summary>Blank values are left out so the entry's defaults apply; booleans are always sent.</summary>
    public ToolRequest BuildRequest(IReadOnlyList<string> files, string outDir, IReadOnlyDictionary<string, string> values)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in Options)
        {
            if (!values.TryGetValue(o.Key, out var v))
                continue;
            if (string.IsNullOrWhiteSpace(v) && o.Control != OptionControl.CheckBox)
                continue;
            options[o.Key] = v.Trim();
        }
        return new ToolRequest(files, outDir, options);
    }

    /// <summary>Success: where the output is. Failure: the runner's message, verbatim.</summary>
    public static string ResultLine(ToolResult r)
    {
        if (r.Ok)
        {
            if (r.Outputs.Count == 1)
                return "Done: " + r.Outputs[0];
            var dir = IoPath.GetDirectoryName(r.Outputs[0]) ?? "";
            return $"Done: {r.Outputs.Count} files in {dir}";
        }
        return r.Error ?? r.Reason ?? "failed";
    }

    /// <summary>
    /// Title, detail line and the old "Done: path" line for the result card. <paramref name="sizeOf"/> returns a file's
    /// length or null when it cannot be read; a missing size is left out of the text, never guessed.
    /// </summary>
    public static ResultSummary Summarize(ToolResult r, Func<string, long?> sizeOf)
    {
        var line = ResultLine(r);
        if (!r.Ok || r.Outputs.Count == 0)
            return new ResultSummary(false, "Could not finish", "", line, null, 0);

        var sizes = r.Outputs.Select(sizeOf).ToList();
        var known = sizes.Where(s => s is not null).Select(s => s!.Value).ToList();
        var sizeText = known.Count == 0 ? null : FormatSize(known.Sum());
        if (r.Outputs.Count == 1)
        {
            var path = r.Outputs[0];
            var ext = IoPath.GetExtension(path).TrimStart('.').ToUpperInvariant();
            var detail = string.Join(" - ", new[] { ext.Length > 0 ? ext + " file" : null, sizeText }.Where(x => x is not null));
            return new ResultSummary(true, IoPath.GetFileName(path), detail, line, path, 1);
        }
        var many = $"{r.Outputs.Count} files";
        return new ResultSummary(true, many + " created", sizeText is null ? many : many + " - " + sizeText, line, r.Outputs[0], r.Outputs.Count);
    }

    public static string? PrimaryOutput(ToolResult r) => r.Ok && r.Outputs.Count > 0 ? r.Outputs[0] : null;

    /// <summary>Arguments for explorer.exe so the file is selected, not its folder opened.</summary>
    public static string ExplorerSelectArguments(string path) => $"/select,\"{path}\"";

    public static string FormatSize(long bytes)
    {
        var inv = CultureInfo.InvariantCulture;
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.#", inv) + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.#", inv) + " MB";
        if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0.#", inv) + " KB";
        return bytes.ToString(inv) + " B";
    }

    private static OptionDescriptor Describe(ToolOption o)
    {
        var (control, choices) = o.Kind switch
        {
            ToolOptionKind.Choice => (OptionControl.ComboBox, o.Choices ?? Array.Empty<string>()),
            ToolOptionKind.Int => (OptionControl.NumberBox, Array.Empty<string>()),
            ToolOptionKind.Percent => (OptionControl.NumberBox, Array.Empty<string>()),
            ToolOptionKind.Bool => (OptionControl.CheckBox, Array.Empty<string>()),
            ToolOptionKind.File => (OptionControl.FileBrowse, Array.Empty<string>()),
            ToolOptionKind.Grid => (OptionControl.GridCombo, o.Choices is { Count: > 0 } ? o.Choices : GridChoices),
            _ => (OptionControl.TextBox, Array.Empty<string>()),
        };

        var min = o.Min;
        var max = o.Max;
        if (o.Kind == ToolOptionKind.Percent)
        {
            min ??= 0;
            max ??= 100;
        }

        var def = o.Default ?? o.Kind switch
        {
            ToolOptionKind.Bool => "false",
            ToolOptionKind.Choice or ToolOptionKind.Grid => choices.Count > 0 ? choices[0] : "",
            _ => "",
        };
        return new OptionDescriptor(o.Key, control, o.Label, def, choices, min, max, o.Kind == ToolOptionKind.PageRange);
    }
}
