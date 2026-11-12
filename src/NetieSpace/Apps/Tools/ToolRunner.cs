using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetieSpace.Apps.Tools;

/// <summary>
/// Runs one tool and refuses to believe it (R-0011): every claimed output must exist and be non-empty,
/// otherwise the result is ok:false naming the path. The tool's own exception text is preserved.
/// </summary>
public static class ToolRunner
{
    private static readonly Regex PageToken = new(@"^-?(\d+|end)(-(\d+|end))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColorRx = new(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex GridRx = new(
        @"^((top|middle|bottom)-(left|center|right)|center|(north|south)?(east|west)?|north|south)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<ToolResult> RunAsync(
        RegisteredTool tool, ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var entry = tool.Entry;
        var engine = entry.Engine.Name;
        var sw = Stopwatch.StartNew();
        ToolResult Fail(ToolOutcome outcome, string error, string? reason = null, IReadOnlyList<string>? outputs = null) =>
            new(false, outcome, entry.Id, engine, outputs ?? Array.Empty<string>(), error, reason, sw.ElapsedMilliseconds);

        if (!tool.Availability.Available || tool.Runner is null)
            return Fail(ToolOutcome.Unavailable, "tool unavailable: " + tool.Availability.Text, tool.Availability.Text);

        var invalid = Validate(entry, request, out var resolved);
        if (invalid is not null)
            return Fail(ToolOutcome.Invalid, invalid);

        IReadOnlyList<string> claimed;
        try
        {
            IoDirectory.CreateDirectory(request.OutDir);
            claimed = await tool.Runner(resolved, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Fail(ToolOutcome.Failed, "cancelled");
        }
        catch (Exception ex)
        {
            return Fail(ToolOutcome.Failed, ex.GetType().Name + ": " + ex.Message);
        }

        if (claimed is null || claimed.Count == 0)
            return Fail(ToolOutcome.Failed, "runner reported no output files");

        foreach (var path in claimed)
        {
            if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
                return Fail(ToolOutcome.Failed, "claimed output is missing: " + path, null, claimed);
            if (new System.IO.FileInfo(path).Length == 0)
                return Fail(ToolOutcome.Failed, "claimed output is zero bytes: " + path, null, claimed);
        }

        progress?.Report(new ToolProgress(100, "done"));
        return new ToolResult(true, ToolOutcome.Ok, entry.Id, engine, claimed, null, null, sw.ElapsedMilliseconds);
    }

    /// <summary>Returns an error message naming the offending file or option key, or null when valid.</summary>
    public static string? Validate(ToolEntry entry, ToolRequest request, out ToolRequest resolved)
    {
        resolved = request;
        var files = request.Files ?? Array.Empty<string>();
        if (files.Count < entry.MinFiles)
            return $"{entry.Id} needs at least {entry.MinFiles} input file(s), got {files.Count}";
        if (files.Count > entry.MaxFiles)
            return $"{entry.Id} accepts at most {entry.MaxFiles} input file(s), got {files.Count}";
        if (string.IsNullOrWhiteSpace(request.OutDir))
            return "output directory is required";

        foreach (var f in files)
        {
            if (!IoFile.Exists(f)) return "input file not found: " + f;
            if (entry.AcceptedTypes.Count > 0)
            {
                var ext = IoPath.GetExtension(f);
                if (!entry.AcceptedTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return $"input type not accepted by {entry.Id}: {f} (accepts {string.Join(" ", entry.AcceptedTypes)})";
            }
        }

        var byKey = entry.Options.ToDictionary(o => o.Key, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in entry.Options)
            if (o.Default is not null) values[o.Key] = o.Default;

        foreach (var kv in request.Options)
        {
            if (!byKey.TryGetValue(kv.Key, out var opt))
                return $"unknown option '{kv.Key}' for {entry.Id}";
            var bad = CheckOption(opt, kv.Value);
            if (bad is not null) return $"option '{kv.Key}': {bad}";
            values[kv.Key] = kv.Value;
        }

        resolved = request with { Options = values };
        return null;
    }

    private static string? CheckOption(ToolOption o, string v)
    {
        switch (o.Kind)
        {
            case ToolOptionKind.Choice:
                return o.Choices is not null && o.Choices.Contains(v, StringComparer.OrdinalIgnoreCase)
                    ? null : $"'{v}' is not one of [{string.Join(", ", o.Choices ?? Array.Empty<string>())}]";
            case ToolOptionKind.Int:
            {
                if (!int.TryParse(v, out var n)) return $"'{v}' is not an integer";
                if (o.Min is int lo && n < lo) return $"{n} is below minimum {lo}";
                if (o.Max is int hi && n > hi) return $"{n} is above maximum {hi}";
                return null;
            }
            case ToolOptionKind.Percent:
            {
                if (!int.TryParse(v, out var n)) return $"'{v}' is not a percent";
                var lo = o.Min ?? 0;
                var hi = o.Max ?? 100;
                return n < lo || n > hi ? $"{n} is outside {lo}..{hi}" : null;
            }
            case ToolOptionKind.Bool:
                return v is "true" or "false" ? null : $"'{v}' is not true or false";
            case ToolOptionKind.Text:
                return null;
            case ToolOptionKind.PageRange:
                return v.Split(',').All(t => PageToken.IsMatch(t.Trim())) ? null : $"'{v}' is not a page range";
            case ToolOptionKind.Color:
                return ColorRx.IsMatch(v) ? null : $"'{v}' is not #RRGGBB or #AARRGGBB";
            case ToolOptionKind.File:
                return IoFile.Exists(v) ? null : "file not found: " + v;
            case ToolOptionKind.Grid:
                return v.Length > 0 && GridRx.IsMatch(v) ? null : $"'{v}' is not a grid position";
            default:
                return "unsupported option kind";
        }
    }
}
