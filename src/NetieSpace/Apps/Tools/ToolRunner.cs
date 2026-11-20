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
    // \z, not $: "$" also matches in front of a trailing newline, which would let "#ffffff\n" through.
    private static readonly Regex PageToken = new(@"^(-?\d+|end)(?:-(\d+|end))?\z", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColorRx = new(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z", RegexOptions.Compiled);
    private static readonly Regex GridRx = new(
        @"^((top|middle|bottom)-(left|center|right)|center|(north|south)?(east|west)?|north|south)\z",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // One lock per output folder: a run's snapshot, its work and its cleanup never overlap with another run's in the
    // same folder, so a failing run cannot delete the finished files of a run that started after its snapshot.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> FolderLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim LockFor(string outDir)
    {
        string key;
        try { key = System.IO.Path.GetFullPath(outDir).TrimEnd('/', '\\'); }
        catch (Exception) { key = outDir; }
        return FolderLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

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

        var gate = LockFor(request.OutDir);
        try { await gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Fail(ToolOutcome.Failed, "cancelled"); }
        try
        {
            return await RunLockedAsync(tool, request, resolved, progress, ct, Fail, entry, engine, sw).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<ToolResult> RunLockedAsync(
        RegisteredTool tool, ToolRequest request, ToolRequest resolved, IProgress<ToolProgress>? progress, CancellationToken ct,
        Func<ToolOutcome, string, string?, IReadOnlyList<string>?, ToolResult> Fail, ToolEntry entry, string engine, Stopwatch sw)
    {
        IReadOnlyList<string> claimed;
        Dictionary<string, (long Length, long Ticks)>? existing = null;
        try
        {
            IoDirectory.CreateDirectory(request.OutDir);
            existing = Snapshot(request.OutDir);
            // 100 means done and only this method says so, after every claimed output has been checked.
            claimed = await tool.Runner!(resolved, progress is null ? null : new Capped(progress), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RemoveNew(request.OutDir, existing);
            return Fail(ToolOutcome.Failed, "cancelled", null, null);
        }
        catch (Exception ex)
        {
            RemoveNew(request.OutDir, existing);
            return Fail(ToolOutcome.Failed, ex.GetType().Name + ": " + ex.Message, null, null);
        }

        if (claimed is null || claimed.Count == 0)
        {
            RemoveNew(request.OutDir, existing);
            return Fail(ToolOutcome.Failed, "runner reported no output files", null, null);
        }

        foreach (var path in claimed)
        {
            string? problem = null;
            if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
                problem = "claimed output is missing: " + path;
            else if (new System.IO.FileInfo(path).Length == 0)
                problem = "claimed output is zero bytes: " + path;
            else if (Untouched(request.OutDir, path, existing))
                problem = "claimed output was not written by this run (it already existed unchanged): " + path;
            if (problem is not null)
            {
                RemoveNew(request.OutDir, existing);
                // Only what is still on disk: a path this method just deleted is not an output.
                return Fail(ToolOutcome.Failed, problem, null, claimed.Where(c => !string.IsNullOrWhiteSpace(c) && IoFile.Exists(c)).ToList());
            }
        }

        progress?.Report(new ToolProgress(100, "done"));
        return new ToolResult(true, ToolOutcome.Ok, entry.Id, engine, claimed, null, null, sw.ElapsedMilliseconds);
    }

    /// <summary>True when a top-level claimed file was in the folder before the run with the same length and mtime.</summary>
    private static bool Untouched(string outDir, string path, Dictionary<string, (long Length, long Ticks)>? before)
    {
        if (before is null) return false;
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var parent = System.IO.Path.GetDirectoryName(full);
            var root = System.IO.Path.GetFullPath(outDir).TrimEnd('/', '\\');
            if (!string.Equals(parent?.TrimEnd('/', '\\'), root, StringComparison.OrdinalIgnoreCase)) return false;
            // The snapshot keys are the entries as EnumerateFileSystemEntries(outDir) spelled them.
            var key = before.Keys.FirstOrDefault(k => string.Equals(System.IO.Path.GetFullPath(k), full, StringComparison.OrdinalIgnoreCase));
            if (key is null) return false;
            var fi = new System.IO.FileInfo(full);
            return before[key] == (fi.Length, fi.LastWriteTimeUtc.Ticks);
        }
        catch (Exception) { return false; }
    }

    /// <summary>Holds a runner's own progress below 100 so that only a verified success reports done.</summary>
    private sealed class Capped : IProgress<ToolProgress>
    {
        private readonly IProgress<ToolProgress> _inner;
        public Capped(IProgress<ToolProgress> inner) { _inner = inner; }
        public void Report(ToolProgress value) =>
            _inner.Report(value.Percent > 99 ? value with { Percent = 99 } : value);
    }

    private static Dictionary<string, (long Length, long Ticks)> Snapshot(string outDir)
    {
        var set = new Dictionary<string, (long Length, long Ticks)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var e in System.IO.Directory.EnumerateFileSystemEntries(outDir))
            {
                try
                {
                    var fi = new System.IO.FileInfo(e);
                    set[e] = fi.Exists ? (fi.Length, fi.LastWriteTimeUtc.Ticks) : (-1, 0);
                }
                catch (Exception) { set[e] = (-1, 0); }
            }
        }
        catch (Exception) { /* an unreadable folder is left alone */ }
        return set;
    }

    /// <summary>
    /// A failed run leaves nothing behind: every top-level file or folder that was not in the output folder before the
    /// run started is removed (a half-written result, or the finished outputs of the earlier files of a batch).
    /// </summary>
    private static void RemoveNew(string outDir, Dictionary<string, (long Length, long Ticks)>? before)
    {
        if (before is null) return;
        try
        {
            foreach (var e in System.IO.Directory.EnumerateFileSystemEntries(outDir).ToList())
            {
                if (before.ContainsKey(e)) continue;
                try
                {
                    if (System.IO.Directory.Exists(e)) System.IO.Directory.Delete(e, true);
                    else System.IO.File.Delete(e);
                }
                catch (Exception) { /* best effort: a file still held open is not worth masking the real error */ }
            }
        }
        catch (Exception) { /* ignore */ }
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
            if (kv.Value is null) return $"option '{kv.Key}': value is missing";
            var bad = CheckOption(opt, kv.Value, out var canonical);
            if (bad is not null) return $"option '{kv.Key}': {bad}";
            values[kv.Key] = canonical;
        }

        resolved = request with { Options = values };
        return null;
    }

    /// <summary>
    /// A page range is checked as far as it can be without the page count: page numbers start at 1 (0 and -0 are
    /// not pages), "-N" counts from the end, and a range of two plain numbers must not run backwards.
    /// </summary>
    private static string? PageRangeProblem(string v)
    {
        var tokens = v.Split(',').Select(t => t.Trim()).ToList();
        foreach (var t in tokens)
        {
            var m = PageToken.Match(t);
            if (!m.Success) return $"'{v}' is not a page range";
            var a = m.Groups[1].Value;
            var b = m.Groups[2].Success ? m.Groups[2].Value : null;
            bool end(string s) => string.Equals(s, "end", StringComparison.OrdinalIgnoreCase);
            bool badNumber(string s) => !end(s) && (!int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) || n == 0);
            if (badNumber(a) || (b is not null && badNumber(b)))
                return $"'{t}' is not a page range (pages start at 1)";
            if (b is not null && !end(a) && !end(b) && !a.StartsWith('-')
                && int.Parse(a, System.Globalization.CultureInfo.InvariantCulture) > int.Parse(b, System.Globalization.CultureInfo.InvariantCulture))
                return $"'{t}' runs backwards";
        }
        return null;
    }

    private static string? CheckOption(ToolOption o, string v, out string canonical)
    {
        canonical = v;
        switch (o.Kind)
        {
            case ToolOptionKind.Choice:
            {
                // Stored in the catalog's spelling, so a runner comparing with == sees the same text every time.
                var hit = o.Choices?.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase));
                if (hit is null) return $"'{v}' is not one of [{string.Join(", ", o.Choices ?? Array.Empty<string>())}]";
                canonical = hit;
                return null;
            }
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
                return PageRangeProblem(v);
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
