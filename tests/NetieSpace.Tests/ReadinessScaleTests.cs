using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NetieSpace.Apps.Tools;
using Xunit.Abstractions;

namespace NetieSpace.Tests;

/// <summary>
/// Scale checks against the real tool engines on this machine: concurrency (1/4/8/16 jobs),
/// same-folder contention, and large inputs (500-page PDF, 4000x3000 picture, 60 s 1080p clip).
/// Numbers are measured, written to NETIE_READINESS_OUT/scale.md, and only coarse correctness is
/// asserted (every job Ok, distinct outputs). Opt-in with NETIE_READINESS=1.
/// Memory is this test process only (VmHWM); ffmpeg children are separate processes and not counted.
/// </summary>
[Collection(IsolatedMeasurementCollection.Name)]
public sealed class ReadinessScaleTests
{
    private readonly ITestOutputHelper _out;
    public ReadinessScaleTests(ITestOutputHelper output) { _out = output; }

    private static readonly ToolRegistry Reg = ToolRegistry.Load(probe: new EngineProbes());
    private static readonly StringBuilder Report = new();
    private static readonly object Gate = new();

    private static void Log(ITestOutputHelper o, string line)
    {
        lock (Gate)
        {
            Report.AppendLine(line);
            Readiness.WriteText("scale.md", "# Scale checks\n\n" + Report);
        }
        o.WriteLine(line);
    }

    private static Dictionary<string, string> Opts(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    private async Task<(int Ok, long WallMs, long P50, long P95, List<string> Errors)> Burst(
        string toolId, int jobs, Func<int, string, List<string>> inputs, Dictionary<string, string> opts, bool sharedOut)
    {
        var tool = Reg.Find(toolId) ?? throw new InvalidOperationException(toolId);
        Assert.True(tool.Availability.Available, toolId + " unavailable here: " + tool.Availability.Text);
        var root = ToolTestSupport.NewTempDir();
        var prepared = Enumerable.Range(0, jobs).Select(i => { var d = IoPath.Combine(root, "in" + i); IoDirectory.CreateDirectory(d); return inputs(i, d); }).ToList();
        var shared = IoPath.Combine(root, "shared");
        var lat = new ConcurrentBag<long>();
        var errs = new ConcurrentBag<string>();
        var outs = new ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, jobs).Select(i => Task.Run(async () =>
        {
            var r = await Readiness.RunWithTimeout(tool, prepared[i], sharedOut ? shared : IoPath.Combine(root, "out" + i), opts, TimeSpan.FromMinutes(5));
            lat.Add(r.ElapsedMs);
            if (r.Ok) foreach (var p in r.Outputs) outs.Add(IoPath.GetFullPath(p));
            else errs.Add(r.Outcome + ": " + r.Error);
        })));
        sw.Stop();
        var sorted = lat.OrderBy(x => x).ToList();
        long Pct(double q) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1)];
        // Distinct outputs: two jobs must never claim the same file.
        Assert.Equal(outs.Count, outs.Distinct(StringComparer.Ordinal).Count());
        try { IoDirectory.Delete(root, true); } catch (Exception) { }
        return (jobs - errs.Count, sw.ElapsedMilliseconds, Pct(0.5), Pct(0.95), errs.ToList());
    }

    [Fact]
    public async Task Concurrency_ladder_pdf_image_video()
    {
        if (!Readiness.Enabled) return;
        Log(_out, "## Concurrency ladder (separate output folders)\n");
        Log(_out, "| tool | jobs | ok | wall ms | p50 ms | p95 ms | jobs/s | peak RSS MB |");
        Log(_out, "|---|---|---|---|---|---|---|---|");
        var cases = new (string Id, Func<int, string, List<string>> In, Dictionary<string, string> O)[]
        {
            ("pdf-merge", (i, d) => new List<string> { PdfKit.Make(d, "a.pdf", 20, "A" + i, extraLines: 20), PdfKit.Make(d, "b.pdf", 20, "B" + i, extraLines: 20) }, Opts()),
            ("pdf-compress", (i, d) => new List<string> { Readiness.MakeImagePdf(d, "c.pdf", 2) }, Opts()),
            ("pdf-extract-text", (i, d) => new List<string> { PdfKit.Make(d, "t.pdf", 30, "T" + i, extraLines: 30) }, Opts()),
            ("img-resize", (i, d) => new List<string> { ImageToolTestSupport.NoisyImage(d, "p.png", "png", 1600, 1200) }, Opts(("mode", "percentage"), ("percent", "50"))),
            ("img-compress", (i, d) => new List<string> { ImageToolTestSupport.NoisyImage(d, "p.jpg", "jpg", 1600, 1200) }, Opts()),
            ("vid-compress", (i, d) => new List<string> { Readiness.MakeNoisyVideo(d, "v.mp4", 5, "640x360", 25) }, Opts(("quality", "low"))),
        };
        var failures = new List<string>();
        foreach (var c in cases)
        {
            foreach (var jobs in new[] { 1, 4, 8, 16 })
            {
                var (ok, wall, p50, p95, errs) = await Burst(c.Id, jobs, c.In, c.O, sharedOut: false);
                var tput = wall == 0 ? 0 : jobs * 1000.0 / wall;
                Log(_out, $"| {c.Id} | {jobs} | {ok}/{jobs} | {wall} | {p50} | {p95} | {tput.ToString("0.00", CultureInfo.InvariantCulture)} | {Readiness.Mb(Readiness.PeakRss())} |");
                if (ok != jobs) failures.Add($"{c.Id} x{jobs}: {string.Join(" / ", errs.Take(2))}");
            }
        }
        Assert.True(failures.Count == 0, "concurrency failures: " + string.Join("; ", failures));
    }

    [Fact]
    public async Task Same_output_folder_contention_is_serialised_not_lost()
    {
        if (!Readiness.Enabled) return;
        Log(_out, "\n## Same output folder, 8 parallel jobs\n");
        var (ok, wall, p50, p95, errs) = await Burst("pdf-merge", 8,
            (i, d) => new List<string> { PdfKit.Make(d, "a" + i + ".pdf", 5, "A" + i), PdfKit.Make(d, "b" + i + ".pdf", 5, "B" + i) },
            Opts(), sharedOut: true);
        Log(_out, $"pdf-merge x8 into one folder: ok {ok}/8, wall {wall} ms, p50 {p50} ms, p95 {p95} ms" + (errs.Count > 0 ? " errors: " + string.Join(" / ", errs.Take(3)) : ""));
        Assert.Equal(8, ok);
    }

    [Fact]
    public async Task Large_inputs()
    {
        if (!Readiness.Enabled) return;
        Log(_out, "\n## Large inputs (single job)\n");
        Log(_out, "| tool | input | ok | ms | input MB | output MB | peak RSS MB | error |");
        Log(_out, "|---|---|---|---|---|---|---|---|");
        var root = ToolTestSupport.NewTempDir();
        var bigPdf = PdfKit.Make(root, "big500.pdf", 500, "BIG", extraLines: 40);
        var picPdf = Readiness.MakeImagePdf(IoPath.Combine(root, "pics"), "pics.pdf", 8);
        var bigPng = ImageToolTestSupport.NoisyImage(root, "big.png", "png", 4000, 3000);
        var bigJpg = ImageToolTestSupport.NoisyImage(root, "big.jpg", "jpg", 4000, 3000);
        var bigVid = Readiness.MakeNoisyVideo(root, "big1080.mp4", 60, "1920x1080", 30);
        var cases = new (string Id, string Label, List<string> In, Dictionary<string, string> O)[]
        {
            ("pdf-merge", "500p + 500p", new List<string> { bigPdf, bigPdf }, Opts()),
            ("pdf-split", "500p every 50", new List<string> { bigPdf }, Opts(("split_mode", "fixed_range"), ("every", "50"))),
            ("pdf-compress", "8 photos 1800x1200", new List<string> { picPdf }, Opts()),
            ("pdf-extract-text", "500p", new List<string> { bigPdf }, Opts()),
            ("pdf-rotate", "500p", new List<string> { bigPdf }, Opts()),
            ("pdf-watermark", "500p", new List<string> { bigPdf }, Opts(("text", "CONFIDENTIAL"))),
            ("img-resize", "4000x3000 png 50%", new List<string> { bigPng }, Opts(("mode", "percentage"), ("percent", "50"))),
            ("img-compress", "4000x3000 jpg", new List<string> { bigJpg }, Opts()),
            ("vid-compress", "60s 1080p30", new List<string> { bigVid }, Opts(("quality", "medium"), ("height", "720"))),
            ("vid-trim", "60s 1080p30 10-40s", new List<string> { bigVid }, Opts(("start", "10"), ("end", "40"))),
            ("vid-to-gif", "60s 1080p30 first 5s", new List<string> { bigVid }, Opts(("duration", "5"))),
        };
        var failures = new List<string>();
        foreach (var c in cases)
        {
            var tool = Reg.Find(c.Id);
            if (tool is null || !tool.Availability.Available)
            {
                Log(_out, $"| {c.Id} | {c.Label} | unavailable | 0 | | | | {tool?.Availability.Text} |");
                continue;
            }
            var outDir = IoPath.Combine(root, "out-" + c.Id);
            var inBytes = c.In.Sum(p => new FileInfo(p).Length);
            var r = await Readiness.RunWithTimeout(tool, c.In, outDir, c.O, TimeSpan.FromMinutes(10));
            var outBytes = r.Outputs.Where(IoFile.Exists).Sum(p => new FileInfo(p).Length);
            Log(_out, $"| {c.Id} | {c.Label} | {(r.Ok ? "ok" : r.Outcome.ToString())} | {r.ElapsedMs} | {Readiness.Mb(inBytes)} | {Readiness.Mb(outBytes)} | {Readiness.Mb(Readiness.PeakRss())} | {(r.Error ?? "").Replace('|', '/')} |");
            if (!r.Ok) failures.Add(c.Id + ": " + r.Error);
        }
        try { IoDirectory.Delete(root, true); } catch (Exception) { }
        Assert.True(failures.Count == 0, "large-input failures: " + string.Join("; ", failures));
    }
}
