using System.Text.RegularExpressions;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>What the button under an error does. The page maps each to one handler; none to "nothing".</summary>
public enum ErrorAction
{
    None,
    /// <summary>Open the file picker.</summary>
    ChooseFile,
    /// <summary>Open the save-folder picker.</summary>
    ChangeFolder,
    /// <summary>Run the same thing again.</summary>
    TryAgain,
    /// <summary>Scroll to the options so a value can be fixed.</summary>
    FixOptions,
    /// <summary>Go to another tool; <see cref="FriendlyError.ActionArg"/> is its id.</summary>
    OpenTool,
    /// <summary>Back to the tool list (the ones that suit the chosen file lead it).</summary>
    ShowTools,
}

/// <summary>
/// One human sentence, at most one action, and the technical text kept apart for a "Details" fold.
/// The engines say what went wrong in their own words ("InvalidOperationException: cannot read x as a PDF: ...");
/// this turns the known cases into something a person who never opened a log can act on, and never hides the original.
/// </summary>
public sealed record FriendlyError(string Sentence, ErrorAction Action, string ActionLabel, string? ActionArg, string Details)
{
    private sealed record Rule(Regex Pattern, Func<Match, string?, FriendlyError> Make);

    private static readonly Regex TypePrefix = new(@"^[A-Za-z]+(?:Exception|Error): ", RegexOptions.Compiled);

    // RunQueue.Combine: "2 of 3 failed: a.pdf: <the runner's message>".
    private static readonly Regex BatchFailure = new(@"^(?<k>\d+) of (?<n>\d+) failed: (?<name>[^:]+): (?<rest>.+)$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Rule[] Rules =
    {
        new(R(@"^cancell?ed$"), (_, _) =>
            new("You stopped this, so nothing was saved.", ErrorAction.TryAgain, "Run it again", null, "")),
        new(R(@"^input file not found: (?<f>.+)$"), (m, _) =>
            new($"Netie cannot find {Quote(Name(m, "f"))}. It may have been moved, renamed or deleted.",
                ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"^input type not accepted by [^:]+: (?<f>.+?) \(accepts (?<t>[^)]*)\)$"), (m, _) =>
            new($"{Quote(Name(m, "f"))} is not a kind of file this tool can use. It works with {m.Groups["t"].Value.Trim()}.",
                ErrorAction.ShowTools, "Show tools that suit this file", null, "")),
        new(R(@"needs at least (?<n>\d+) input file"), (m, _) =>
            new($"Add {Plural(int.Parse(m.Groups["n"].Value), "file")} to use this tool.", ErrorAction.ChooseFile, "Add files", null, "")),
        new(R(@"accepts at most (?<n>\d+) input file"), (m, _) =>
            new($"This tool takes {Plural(int.Parse(m.Groups["n"].Value), "file")} at a time. Remove a few and try again.",
                ErrorAction.ChooseFile, "Choose files", null, "")),
        new(R(@"output directory is required|^no output folder|^not a valid folder|^not a folder|^cannot write to"), (_, _) =>
            new("Netie needs a folder to save into, and this one cannot be used. Pick another.", ErrorAction.ChangeFolder, "Choose a save folder", null, "")),
        new(R(@"^option '(?<k>[^']+)': (?<why>.+)$"), (m, _) =>
            new($"The {Quote(OptionName(m.Groups["k"].Value))} setting is not right: {AsSentence(m.Groups["why"].Value)}",
                ErrorAction.FixOptions, "Go to the settings", null, "")),
        new(R(@"password protected|is encrypted|password required|needs a password"), (_, id) =>
            new("This PDF is locked with a password. Unlock it first, then come back.",
                id == "pdf-unlock" ? ErrorAction.None : ErrorAction.OpenTool, "Unlock PDF", "pdf-unlock", "")),
        new(R(@"already carries a digital signature"), (_, _) =>
            new("This PDF is already signed, and changing it would break the signature. Work on a copy of it instead.",
                ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"no extractable text layer"), (_, _) =>
            new("This PDF is a scan, so there is no text in it to read yet. Make it searchable first.",
                ErrorAction.OpenTool, "Make PDF searchable", "pdf-ocr", "")),
        new(R(@"cannot read .{0,200}as a pdf|cannot open|damaged|not a readable|cannot decode|cannot read the file|ffprobe could not read|could not read the duration"), (_, id) =>
            id is not null && id.StartsWith("pdf-", StringComparison.Ordinal) && id != "pdf-repair"
                ? new("This file looks damaged, so Netie cannot read it. Repairing it first often works.", ErrorAction.OpenTool, "Repair PDF", "pdf-repair", "")
                : new("This file looks damaged, so Netie cannot read it. Try a different copy of it.", ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"no pages"), (_, _) =>
            new("This PDF has no pages in it, so there is nothing to work on.", ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"ffmpeg was not found|ffprobe was not found|could not start ffmpeg|could not start ffprobe"), (_, _) =>
            new("The video engine (FFmpeg) is missing on this PC, so video tools cannot run yet.", ErrorAction.ShowTools, "Back to the tools", null, "")),
        new(R(@"ffmpeg failed"), (_, _) =>
            new("The video engine could not process this file. It may be damaged or use a format that is not supported.",
                ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"access to the path|unauthorizedaccess|permission denied|read-only|is denied"), (_, _) =>
            new("Windows would not let Netie save there. Pick a folder that is yours, such as Documents.", ErrorAction.ChangeFolder, "Choose a save folder", null, "")),
        new(R(@"being used by another process|sharing violation|locked by another"), (_, _) =>
            new("Another program has this file open. Close it there, then try again.", ErrorAction.TryAgain, "Try again", null, "")),
        new(R(@"not enough space|disk is full|no space left"), (_, _) =>
            new("The disk is full. Free some space or save somewhere else.", ErrorAction.ChangeFolder, "Choose a save folder", null, "")),
        new(R(@"claimed output is missing|claimed output is zero bytes|was not written by this run|runner reported no output"), (_, _) =>
            new("The tool finished but did not make a usable file, so nothing was kept. Try again, or save somewhere else.",
                ErrorAction.TryAgain, "Try again", null, "")),
        new(R(@"out of memory|too large|larger than the limit|too elongated|longer than"), (_, _) =>
            new("This file is too big or too long for this tool on this PC. Try a smaller one.", ErrorAction.ChooseFile, "Choose another file", null, "")),
        new(R(@"not built in this version|not supported"), (_, _) =>
            new("This tool cannot do that yet. The other tools in the list may be able to.", ErrorAction.ShowTools, "Back to the tools", null, "")),
        new(R(@"web view"), (_, _) =>
            new("The page could not be drawn. Check that Microsoft Edge WebView2 is installed, then try again.", ErrorAction.TryAgain, "Try again", null, "")),
    };

    /// <summary>The failure of a run, as a person reads it. The tool id lets a rule pick the next tool.</summary>
    public static FriendlyError ForResult(ToolResult r)
    {
        var raw = (r.Error ?? r.Reason ?? "failed").Trim();
        if (r.Outcome == ToolOutcome.Unavailable)
            return ForUnavailable(r.Reason ?? raw, r.ToolId, null, raw);
        return ForText(raw, r.ToolId);
    }

    public static FriendlyError ForText(string raw, string? toolId)
    {
        var text = TypePrefix.Replace(raw.Trim(), "");
        var details = DetailsOf(raw, toolId);
        var batch = BatchFailure.Match(text);
        if (batch.Success)
        {
            var inner = ForText(batch.Groups["rest"].Value, toolId);
            return inner with
            {
                Sentence = $"{batch.Groups["k"].Value} of {batch.Groups["n"].Value} files could not be finished. "
                    + $"The first was {Quote(batch.Groups["name"].Value.Trim())}: {inner.Sentence}",
                Details = details,
            };
        }
        foreach (var rule in Rules)
        {
            var m = rule.Pattern.Match(text);
            if (m.Success)
                return rule.Make(m, toolId) with { Details = details };
        }
        return new FriendlyError(Fallback(text), ErrorAction.TryAgain, "Try again", null, details);
    }

    /// <summary>
    /// A tool that cannot run on this PC: what it needs, in plain words, and a way forward. When another tool of the same
    /// kind that takes the same files can run (<paramref name="alternative"/>), the way forward is that tool.
    /// </summary>
    public static FriendlyError ForUnavailable(string availabilityText, string? toolId, HomeTile? alternative, string? raw = null)
    {
        var details = DetailsOf(raw ?? availabilityText, toolId);
        var need = NeedOf(availabilityText);
        string sentence;
        if (need is not null)
            sentence = $"This tool needs {need}, which is not set up on this PC.";
        else if (availabilityText.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            sentence = "This tool needs an online service, and Netie keeps your files on this PC.";
        else if (availabilityText.Contains("not built", StringComparison.OrdinalIgnoreCase))
            sentence = "This tool is not ready in this version of Netie.";
        else if (availabilityText.Contains("disclose", StringComparison.OrdinalIgnoreCase))
            sentence = "This tool would send your file to an online service, so it stays off until you agree to that.";
        else if (availabilityText.StartsWith(ToolAvailabilityRules.PrefixLicence, StringComparison.Ordinal))
            sentence = "This tool is not included in this version of Netie.";
        else
            sentence = AsSentence(availabilityText);

        return alternative is not null
            ? new FriendlyError(sentence + $" {Quote(alternative.Name)} does something close.", ErrorAction.OpenTool, $"Use {alternative.Name} instead", alternative.ToolId, details)
            : new FriendlyError(sentence, ErrorAction.ShowTools, "Back to all tools", null, details);
    }

    /// <summary>The few words that fit on a tile under the tool name: "Needs Microsoft Word".</summary>
    public static string Short(string availabilityText)
    {
        var need = NeedOf(availabilityText);
        if (need is not null)
            return "Needs " + need;
        if (availabilityText.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            return "Needs an online service";
        if (availabilityText.Contains("not built", StringComparison.OrdinalIgnoreCase))
            return "Not ready yet";
        if (availabilityText.Contains("disclose", StringComparison.OrdinalIgnoreCase))
            return "Off until you agree";
        if (availabilityText.StartsWith(ToolAvailabilityRules.PrefixLicence, StringComparison.Ordinal))
            return "Not included";
        return AsSentence(availabilityText).TrimEnd('.');
    }

    /// <summary>The first available tool of the same category that takes the same kinds of file, or null.</summary>
    public static HomeTile? AlternativeFor(HomeTile tile, IEnumerable<HomeTile> all) =>
        all.FirstOrDefault(t =>
            t.Available
            && t.ToolId != tile.ToolId
            && t.Category == tile.Category
            && t.Types is { Count: > 0 } theirs
            && tile.Types is { Count: > 0 } mine
            && theirs.Intersect(mine, StringComparer.OrdinalIgnoreCase).Any());

    /// <summary>"Word COM" is the catalog's name for the engine; a person knows it as Microsoft Word.</summary>
    public static string? NeedOf(string availabilityText)
    {
        if (!availabilityText.StartsWith(ToolAvailabilityRules.PrefixEngineMissing, StringComparison.Ordinal))
            return null;
        var text = availabilityText[ToolAvailabilityRules.PrefixEngineMissing.Length..].Trim();
        var lower = text.ToLowerInvariant();
        if (lower.StartsWith("word")) return "Microsoft Word";
        if (lower.StartsWith("excel")) return "Microsoft Excel";
        if (lower.StartsWith("powerpoint")) return "Microsoft PowerPoint";
        if (lower.StartsWith("ffmpeg")) return "FFmpeg (the video engine)";
        if (lower.StartsWith("tesseract")) return "the Tesseract text-recognition engine";
        if (lower.StartsWith("webview2")) return "the Microsoft Edge WebView2 runtime";
        if (lower.StartsWith("python")) return "a Python runtime";
        if (lower.StartsWith("whisper")) return "the whisper speech engine";
        return text.Length == 0 ? null : text;
    }

    private static string DetailsOf(string raw, string? toolId) =>
        string.IsNullOrWhiteSpace(toolId) ? raw.Trim() : $"{raw.Trim()}\n(tool: {toolId})";

    private static string Fallback(string text)
    {
        var one = Regex.Replace(text, @"\s+", " ").Trim();
        var plain = one.Length is > 0 and <= 140 && !Regex.IsMatch(one, @"Exception|0x[0-9A-Fa-f]+| at [A-Za-z.]+\(|[A-Za-z]:\\");
        return plain
            ? AsSentence(one)
            : "This tool could not finish. Try again, or pick a different file.";
    }

    private static string AsSentence(string s)
    {
        var t = s.Trim().TrimEnd('.', ';', ':');
        if (t.Length == 0) return "Something went wrong.";
        return char.ToUpperInvariant(t[0]) + t[1..] + ".";
    }

    private static string Name(Match m, string group)
    {
        var v = m.Groups[group].Value.Trim().Trim('"');
        var cut = Math.Max(v.LastIndexOf('\\'), v.LastIndexOf('/'));
        return cut >= 0 ? v[(cut + 1)..] : v;
    }

    private static string Quote(string s) => "\"" + s + "\"";
    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>"page-range" or "dpi" as a label a person reads.</summary>
    private static string OptionName(string key) => key.Replace('-', ' ').Replace('_', ' ');

    private static Regex R(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
