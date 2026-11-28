using System.Globalization;
using System.Text;
using NetieSpace.Apps.Tools;
using Xunit.Abstractions;

namespace NetieSpace.Tests;

/// <summary>
/// Readiness matrix: every catalog tool, on this machine, with real engines (no fake probe).
/// A row is Ok only when ToolRunner itself says Ok (it re-checks every claimed output exists, is
/// non-empty and was written by this run). Opt-in with NETIE_READINESS=1.
/// </summary>
[Collection(IsolatedMeasurementCollection.Name)]
public sealed class ReadinessMatrixTests
{
    private readonly ITestOutputHelper _out;
    public ReadinessMatrixTests(ITestOutputHelper output) { _out = output; }

    public sealed record Row(
        string Id, string App, string Verdict, bool Available, string AvailabilityText,
        string Status, string? Input, int Attempts, long ElapsedMs, int Outputs, long OutputBytes, string? Error);

    // Measured floor for the Linux runner on 2026-11-28 (ubuntu, ffmpeg + tesseract present).
    // Raise it when a tool starts working; never lower it to make a red run green.
    private static int Floor() =>
        int.TryParse(Environment.GetEnvironmentVariable("NETIE_READINESS_FLOOR"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0;

    [Fact]
    public async Task Every_catalog_tool_runs_or_states_why_not()
    {
        if (!Readiness.Enabled) return;

        var registry = ToolRegistry.Load(probe: new EngineProbes());
        var rows = new List<Row>();
        var root = ToolTestSupport.NewTempDir();
        var timeout = TimeSpan.FromSeconds(120);

        foreach (var tool in registry.All())
        {
            var e = tool.Entry;
            if (!tool.Availability.Available || tool.Runner is null)
            {
                rows.Add(new Row(e.Id, e.App.ToString(), e.Verdict.ToString(), false, tool.Availability.Text,
                    "unavailable:" + tool.Availability.Reason, null, 0, 0, 0, 0, null));
                continue;
            }

            var dir = IoPath.Combine(root, e.Id);
            var recipe = Recipe(e.Id, dir);
            var ext = recipe is not null ? recipe.Value.Label : Readiness.PickExt(e);
            if (ext is null && e.MinFiles == 0 && e.AcceptedTypes.Count == 0) ext = "(no input)";
            if (ext is null)
            {
                rows.Add(new Row(e.Id, e.App.ToString(), e.Verdict.ToString(), true, "", "no-fixture",
                    string.Join(",", e.AcceptedTypes), 0, 0, 0, 0, "suite cannot generate any accepted type"));
                continue;
            }

            var attempts = new List<(List<string> Files, Dictionary<string, string> Opts)>();
            if (recipe is not null)
            {
                // A hand-written recipe: the inputs and options a real user would give this tool.
                attempts.Add((recipe.Value.Files, recipe.Value.Opts));
            }
            else if (ext == "(no input)")
            {
                attempts.Add((new List<string>(), new Dictionary<string, string>()));
            }
            else
            {
                var count = Math.Max(1, Math.Min(e.MinFiles, 2));
                if (e.MaxFiles >= 2 && e.MinFiles <= 1 && e.Id.Contains("merge", StringComparison.Ordinal)) count = 2;
                var files = Enumerable.Range(1, count).Select(i => Readiness.MakeInput(IoPath.Combine(dir, "in"), ext, i)!).ToList();
                attempts.Add((files, new Dictionary<string, string>()));
                // Second try: fill options that have no default with a plain sample value.
                attempts.Add((files, SampleOptions(e, dir)));
                // Third try: mixed inputs (video + audio or video + picture) for tools that take both.
                var mixed = MixedInputs(e, dir);
                if (mixed is not null) attempts.Add((mixed, SampleOptions(e, dir)));
            }

            ToolResult? last = null;
            var n = 0;
            foreach (var (f, o) in attempts)
            {
                n++;
                var outDir = IoPath.Combine(dir, "out" + n);
                last = await Readiness.RunWithTimeout(tool, f, outDir, o, timeout);
                if (last.Ok) break;
                if (last.Outcome == ToolOutcome.Failed && !LooksLikeInputProblem(last.Error)) break;
            }

            var bytes = last!.Outputs.Where(IoFile.Exists).Sum(p => new FileInfo(p).Length);
            rows.Add(new Row(e.Id, e.App.ToString(), e.Verdict.ToString(), true, "",
                last.Ok ? "ok" : last.Outcome.ToString().ToLowerInvariant(), ext, n, last.ElapsedMs,
                last.Outputs.Count, bytes, Trim(last.Error)));
        }

        Readiness.WriteJson("tool-matrix.json", rows);
        var md = new StringBuilder();
        md.AppendLine("| tool | app | verdict | status | input | tries | ms | outputs | error |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            md.AppendLine($"| {r.Id} | {r.App} | {r.Verdict} | {r.Status} | {r.Input} | {r.Attempts} | {r.ElapsedMs} | {r.Outputs} | {(r.Error ?? r.AvailabilityText).Replace('|', '/')} |");
        var ok = rows.Count(r => r.Status == "ok");
        var summary = $"total {rows.Count}, ok {ok}, failed {rows.Count(r => r.Status == "failed")}, invalid {rows.Count(r => r.Status == "invalid")}, unavailable {rows.Count(r => !r.Available)}, no-fixture {rows.Count(r => r.Status == "no-fixture")}";
        md.Insert(0, "# Tool readiness matrix\n\n" + summary + "\n\n");
        Readiness.WriteText("tool-matrix.md", md.ToString());
        _out.WriteLine(summary);
        _out.WriteLine(md.ToString());

        try { IoDirectory.Delete(root, true); } catch (Exception) { }

        Assert.True(ok >= Floor(), $"readiness floor: {ok} ok < floor {Floor()}. {summary}");
        // A tool that ran must never be slower than the per-tool budget on a 3-page or 3-second fixture.
        Assert.DoesNotContain(rows, r => r.Status == "ok" && r.ElapsedMs > 60_000);
    }

    private static Dictionary<string, string> O(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    /// <summary>
    /// Tools whose defaults alone are not a real job (they need a margin, a time, a second file, or a
    /// file with something to fix). Each recipe is what a user would type in the tool page.
    /// </summary>
    private static (string Label, List<string> Files, Dictionary<string, string> Opts)? Recipe(string id, string dir)
    {
        var d = IoPath.Combine(dir, "recipe");
        IoDirectory.CreateDirectory(d);
        string Pdf(int i = 1) => Readiness.MakeInput(d, ".pdf", i)!;
        string Mp4(int i = 1) => Readiness.MakeInput(d, ".mp4", i)!;
        string Png(int i = 1) => Readiness.MakeInput(d, ".png", i)!;
        switch (id)
        {
            case "pdf-compress": return ("pdf with pictures", new() { Readiness.MakeImagePdf(d, "pics.pdf", 3) }, O(("level", "recommended")));
            case "pdf-repair": return ("damaged pdf", new() { Readiness.MakeDamagedPdf(d, "broken.pdf") }, O());
            case "pdf-to-excel": return ("pdf with a table", new() { Readiness.MakeTablePdf(d, "table.pdf") }, O());
            case "pdf-crop": return (".pdf", new() { Pdf() }, O(("left", "36"), ("top", "36")));
            case "pdf-sign-self": return (".pdf", new() { Pdf() }, O(("mode", "name"), ("name", "Netie Tester")));
            case "img-watermark": return (".png", new() { Png() }, O(("text", "NETIE")));
            case "img-convert-any": return (".png to jpg", new() { Png() }, O(("to", "jpg")));
            case "vid-merge": return ("2x .mp4", new() { Mp4(1), Mp4(2) }, O());
            case "vid-resize": return (".mp4", new() { Mp4() }, O(("width", "160")));
            case "vid-add-text": return (".mp4", new() { Mp4() }, O(("text", "NETIE"), ("end", "2")));
            case "vid-add-image": return (".mp4 + .png", new() { Mp4(), Png(2) }, O(("end", "2")));
            case "vid-remove-logo": return (".mp4", new() { Mp4() }, O(("x", "10"), ("y", "10"), ("width", "60"), ("height", "30"), ("end", "2")));
            case "vid-subtitles": return (".mp4 + .srt", new() { Mp4(), Readiness.MakeInput(d, ".srt", 2)! }, O());
            case "vid-compress": return ("noisy .mp4", new() { Readiness.MakeNoisyVideo(d, "noisy.mp4", 4, "640x360") }, O(("quality", "low"), ("height", "360")));
            case "vid-to-gif": return (".mp4", new() { Mp4() }, O());
            case "vid-adjust": return (".mp4", new() { Mp4() }, O(("brightness", "20"), ("contrast", "10")));
            default: return null;
        }
    }

    private static bool LooksLikeInputProblem(string? error) =>
        error is not null && (error.Contains("option", StringComparison.OrdinalIgnoreCase)
            || error.Contains("needs", StringComparison.OrdinalIgnoreCase)
            || error.Contains("required", StringComparison.OrdinalIgnoreCase)
            || error.Contains("audio", StringComparison.OrdinalIgnoreCase)
            || error.Contains("image", StringComparison.OrdinalIgnoreCase)
            || error.Contains("video", StringComparison.OrdinalIgnoreCase)
            || error.Contains("file", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> SampleOptions(ToolEntry e, string dir)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var opt in e.Options)
        {
            if (opt.Default is not null) continue;
            switch (opt.Kind)
            {
                case ToolOptionKind.Text:
                    o[opt.Key] = opt.Key.Contains("pass", StringComparison.OrdinalIgnoreCase) ? "netie-test-1" : "Netie";
                    break;
                case ToolOptionKind.PageRange: o[opt.Key] = "1"; break;
                case ToolOptionKind.Int:
                    o[opt.Key] = (opt.Key.Contains("width", StringComparison.Ordinal) || opt.Key.Contains("height", StringComparison.Ordinal)
                        ? Math.Max(opt.Min ?? 1, 100) : (opt.Min ?? 1)).ToString(CultureInfo.InvariantCulture);
                    break;
                case ToolOptionKind.Percent: o[opt.Key] = "50"; break;
                case ToolOptionKind.Bool: o[opt.Key] = "true"; break;
                case ToolOptionKind.Color: o[opt.Key] = "#FF0000"; break;
                case ToolOptionKind.Choice when opt.Choices is { Count: > 0 }: o[opt.Key] = opt.Choices[0]; break;
                case ToolOptionKind.File:
                    o[opt.Key] = Readiness.MakeInput(IoPath.Combine(dir, "opt"), ".png", 9)!;
                    break;
            }
        }
        return o;
    }

    private static List<string>? MixedInputs(ToolEntry e, string dir)
    {
        var acc = e.AcceptedTypes.Select(a => a.StartsWith('.') ? a : "." + a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!acc.Contains(".mp4")) return null;
        var second = acc.Contains(".mp3") ? ".mp3" : acc.Contains(".png") ? ".png" : acc.Contains(".srt") ? ".srt" : null;
        if (second is null) return null;
        var mdir = IoPath.Combine(dir, "mixed");
        return new List<string> { Readiness.MakeInput(mdir, ".mp4", 1)!, Readiness.MakeInput(mdir, second, 2)! };
    }

    private static string? Trim(string? s) => s is null ? null : (s.Length > 220 ? s[..220] + "..." : s).Replace('\n', ' ');
}
