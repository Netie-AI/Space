using System.Diagnostics;
using System.Text.Json;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;
using Xunit.Abstractions;

namespace NetieSpace.Tests;

internal sealed class FakePlatform : IPlatformProbes
{
    public Func<(bool, string)> OcrF = () => (true, "");
    public Func<(bool, string)> SpeechF = () => (true, "");
    public Func<(bool, string)> WebView2F = () => (true, "");
    public Func<string, (bool, string)> ProgIdF = _ => (true, "");
    public (bool Ok, string Detail) Ocr() => OcrF();
    public (bool Ok, string Detail) Speech() => SpeechF();
    public (bool Ok, string Detail) WebView2() => WebView2F();
    public (bool Ok, string Detail) ProgId(string progId, string engineName) => ProgIdF(progId);
}

internal sealed class FakeFfmpeg : IFfmpegInfo
{
    public string? Exe = "/fake/ffmpeg";
    public bool Runnable = true;
    public HashSet<string>? FilterSet = new() { "scale", "crop" };
    public string? Locate() => Exe;
    public bool Runs(string exe) => Runnable;
    public IReadOnlySet<string>? Filters(string exe) => Runnable ? FilterSet : null;
}

internal sealed class FakeFfmpegSlowFirst : IFfmpegInfo
{
    private readonly Func<int> _onRun;
    public FakeFfmpegSlowFirst(Func<int> onRun) { _onRun = onRun; }
    public string? Locate() => "/fake/ffmpeg";
    public bool Runs(string exe) { if (_onRun() == 1) Thread.Sleep(1500); return true; }
    public IReadOnlySet<string>? Filters(string exe) => null;
}

public class ToolProbeTests
{
    private readonly ITestOutputHelper _out;
    public ToolProbeTests(ITestOutputHelper output) { _out = output; }

    private static (bool Ok, string Detail) Real(FakePlatform? p = null, IFfmpegInfo? f = null, EngineRef? e = null, int budget = 2000)
        => new EngineProbes(p ?? new FakePlatform(), f ?? new FakeFfmpeg(), budget).Probe(e!);

    // ---- OCR / speech / WebView2 / Office: the reason names what to install --------------------

    [Fact]
    public void Ocr_without_a_language_pack_is_missing_and_names_the_pack()
    {
        var p = new FakePlatform { OcrF = () => (false, "Windows OCR language pack") };
        var (ok, detail) = Real(p, e: Engines.WinRtOcr);
        Assert.False(ok);
        Assert.Equal("Windows OCR language pack", detail);
    }

    [Fact]
    public void Ocr_with_a_language_pack_is_present() => Assert.True(Real(e: Engines.WinRtOcr).Ok);

    [Fact]
    public void Speech_without_a_voice_is_missing_and_names_the_voice()
    {
        var p = new FakePlatform { SpeechF = () => (false, "") };
        var (ok, detail) = Real(p, e: Engines.WinRtSpeech);
        Assert.False(ok);
        Assert.Equal("Windows speech voice", detail);
    }

    [Fact]
    public void WebView2_missing_runtime_is_reported()
    {
        var p = new FakePlatform { WebView2F = () => (false, "WebView2 runtime") };
        Assert.Equal((false, "WebView2 runtime"), Real(p, e: Engines.WebView2));
        Assert.True(Real(e: Engines.WebView2).Ok);
    }

    [Theory]
    [InlineData("Word COM", "Word.Application")]
    [InlineData("Excel COM", "Excel.Application")]
    [InlineData("PowerPoint COM", "PowerPoint.Application")]
    public void Office_is_probed_by_its_own_progid(string engineName, string progId)
    {
        var asked = new List<string>();
        var p = new FakePlatform { ProgIdF = id => { asked.Add(id); return id == progId ? (true, "") : (false, engineName); } };
        var engine = Engines.All.Single(e => e.Name == engineName);
        Assert.True(Real(p, e: engine).Ok);
        Assert.Equal(new[] { progId }, asked);

        var none = new FakePlatform { ProgIdF = _ => (false, engineName) };
        Assert.Equal((false, engineName), Real(none, e: engine));
    }

    // ---- throws / hangs / lies --------------------------------------------------------------------

    [Fact]
    public void A_probe_that_throws_is_missing_never_present()
    {
        var p = new FakePlatform { OcrF = () => throw new InvalidOperationException("boom") };
        var (ok, detail) = Real(p, e: Engines.WinRtOcr);
        Assert.False(ok);
        Assert.Equal(Engines.WinRtOcr.Name, detail);
    }

    [Fact]
    public void A_probe_that_hangs_is_missing_after_the_budget_with_the_reason()
    {
        var gate = new ManualResetEventSlim();
        var p = new FakePlatform { SpeechF = () => { gate.Wait(10_000); return (true, ""); } };
        var sw = Stopwatch.StartNew();
        var (ok, detail) = Real(p, e: Engines.WinRtSpeech, budget: 300);
        sw.Stop();
        gate.Set();
        Assert.False(ok);
        Assert.Contains("probe timed out", detail);
        Assert.StartsWith(Engines.WinRtSpeech.Name, detail);
        Assert.InRange(sw.ElapsedMilliseconds, 250, 2500);
    }

    [Fact]
    public void Probes_run_on_their_own_thread_so_a_busy_thread_pool_cannot_time_them_out()
    {
        bool? onPool = null;
        var p = new FakePlatform { OcrF = () => { onPool = Thread.CurrentThread.IsThreadPoolThread; return (true, ""); } };
        Assert.True(Real(p, e: Engines.WinRtOcr).Ok);
        Assert.False(onPool);
    }

    [Fact]
    public void A_process_probe_gets_a_second_attempt_after_a_cold_first_start_but_others_do_not()
    {
        var starts = 0;
        var f = new FakeFfmpegSlowFirst(() => Interlocked.Increment(ref starts));
        Assert.True(Real(f: f, e: Engines.Ffmpeg, budget: 300).Ok);
        Assert.Equal(2, starts);

        var polls = 0;
        var p = new FakePlatform { OcrF = () => { Interlocked.Increment(ref polls); Thread.Sleep(1500); return (true, ""); } };
        Assert.False(Real(p, e: Engines.WinRtOcr, budget: 200).Ok);
        Assert.Equal(1, polls);
    }

    [Fact]
    public void A_timed_out_answer_is_asked_again_after_the_retry_window_but_a_definite_one_never_is()
    {
        var calls = 0;
        var probe = new FuncProbe(e => e.Name == "rembg"
            ? (Interlocked.Increment(ref calls) == 1 ? (false, "rembg (probe timed out)") : (true, ""))
            : (true, ""));
        var reg = ToolRegistry.Load(probe: probe);
        Assert.Equal("engine missing: rembg (probe timed out)", reg.Find("img-remove-bg")!.Availability.Text);
        Assert.Equal("engine missing: rembg (probe timed out)", reg.Find("img-remove-bg")!.Availability.Text);
        Assert.Equal(1, calls);                       // inside the window it is not re-run
        Thread.Sleep(ToolRegistry.TimeoutRetryMs + 500);
        Assert.True(reg.Find("img-remove-bg")!.Availability.Available || reg.Find("img-remove-bg")!.Availability.Reason == UnavailableReason.NotBuilt);
        Assert.Equal(2, calls);
        reg.Find("img-remove-bg"); reg.All();
        Assert.Equal(2, calls);                       // a definite answer stays cached
    }

    [Fact]
    public void The_default_budget_is_two_seconds() => Assert.Equal(2000, EngineProbes.ProbeTimeoutMs);

    [Fact]
    public void A_registry_probe_that_throws_makes_the_tool_unavailable_with_the_engine_name()
    {
        var reg = ToolRegistry.Load(probe: new FuncProbe(e => e.Name == "rembg" ? throw new Exception("x") : (true, "")));
        Assert.Equal("engine missing: rembg", reg.Find("img-remove-bg")!.Availability.Text);
        Assert.True(reg.Find("pdf-extract-text")!.Availability.Available);
    }

    // ---- FFmpeg and the specific filters -----------------------------------------------------------

    [Fact]
    public void Ffmpeg_not_found_makes_filter_engines_missing_as_FFmpeg()
    {
        var f = new FakeFfmpeg { Exe = null };
        Assert.Equal((false, "FFmpeg"), Real(f: f, e: Engines.FfmpegDrawtext));
        Assert.Equal((false, "FFmpeg"), Real(f: f, e: Engines.Ffmpeg));
    }

    [Fact]
    public void Ffmpeg_that_does_not_run_is_missing()
    {
        var f = new FakeFfmpeg { Runnable = false };
        Assert.Equal((false, "FFmpeg"), Real(f: f, e: Engines.Ffmpeg));
        Assert.Equal((false, "FFmpeg"), Real(f: f, e: Engines.FfmpegSubtitles));
    }

    [Fact]
    public void Drawtext_filter_missing_names_the_filter_and_present_when_built_in()
    {
        var without = new FakeFfmpeg();
        Assert.Equal((false, "FFmpeg filter drawtext"), Real(f: without, e: Engines.FfmpegDrawtext));
        var with = new FakeFfmpeg { FilterSet = new() { "drawtext" } };
        Assert.True(Real(f: with, e: Engines.FfmpegDrawtext).Ok);
    }

    [Fact]
    public void Stabilize_needs_both_vidstab_filters_or_deshake()
    {
        var half = new FakeFfmpeg { FilterSet = new() { "vidstabdetect" } };
        Assert.False(Real(f: half, e: Engines.FfmpegVidstab).Ok);
        var both = new FakeFfmpeg { FilterSet = new() { "vidstabdetect", "vidstabtransform" } };
        Assert.True(Real(f: both, e: Engines.FfmpegVidstab).Ok);
        var deshake = new FakeFfmpeg { FilterSet = new() { "deshake" } };
        Assert.True(Real(f: deshake, e: Engines.FfmpegVidstab).Ok);
        var (ok, detail) = Real(f: new FakeFfmpeg(), e: Engines.FfmpegVidstab);
        Assert.False(ok);
        Assert.Equal("FFmpeg filter vidstabdetect+vidstabtransform or deshake", detail);
    }

    [Fact]
    public void Filter_list_parser_reads_lf_and_crlf_output_and_skips_the_legend()
    {
        var lf = "Filters:\n  T.. = Timeline support\n  A = Audio input/output\n ... acopy             A->A       Copy.\n T.C drawtext          V->V       Draw text.\n ... vidstabdetect     V->V       Extract.\n ..C |sink  |->V  x\n";
        foreach (var text in new[] { lf, lf.Replace("\n", "\r\n") })
        {
            var set = FfmpegInfo.ParseFilters(text)!;
            Assert.Contains("drawtext", set);
            Assert.Contains("vidstabdetect", set);
            Assert.Contains("acopy", set);
            Assert.DoesNotContain("Timeline", set);
            Assert.DoesNotContain("A", set);
        }
        // A different flags column width (newer FFmpeg) still parses.
        var wide = FfmpegInfo.ParseFilters("Filters:\n TSC. drawtext          V->V       Draw text.\n .... subtitles        V->V       Render.\n")!;
        Assert.Contains("drawtext", wide);
        Assert.Contains("subtitles", wide);
        Assert.Null(FfmpegInfo.ParseFilters("garbage"));
    }

    [Fact]
    public void Unreadable_filter_list_is_missing_not_assumed_present()
    {
        var f = new FakeFfmpeg { FilterSet = null };
        Assert.Equal((false, "FFmpeg"), Real(f: f, e: Engines.FfmpegSubtitles));
    }

    // ---- the catalog joins the finer keys -----------------------------------------------------------

    private static IEngineProbe Missing(params string[] names) =>
        new FuncProbe(e => names.Contains(e.Name) ? (false, e.Name) : (true, ""));

    [Fact]
    public void Pdf_ocr_and_vid_tts_follow_the_finer_engines_while_other_winrt_tools_stay_available()
    {
        var reg = ToolRegistry.Load(probe: Missing("Windows OCR language pack", "Windows speech voice"));
        Assert.Equal("engine missing: Windows OCR language pack", reg.Find("pdf-ocr")!.Availability.Text);
        Assert.Equal("engine missing: Windows speech voice", reg.Find("vid-tts")!.Availability.Text);
        Assert.NotEqual(UnavailableReason.EngineMissing, reg.Find("pdf-to-jpg")!.Availability.Reason);
    }

    [Fact]
    public void Vid_stabilize_and_text_overlays_follow_their_ffmpeg_filters_while_trim_stays_available()
    {
        var reg = ToolRegistry.Load(probe: Missing(Engines.FfmpegVidstab.Name, Engines.FfmpegDrawtext.Name, Engines.FfmpegSubtitles.Name));
        Assert.StartsWith("engine missing: FFmpeg filter", reg.Find("vid-stabilize")!.Availability.Text);
        Assert.Equal("engine missing: FFmpeg filter drawtext", reg.Find("vid-add-text")!.Availability.Text);
        Assert.Equal("engine missing: FFmpeg filter subtitles", reg.Find("vid-subtitles")!.Availability.Text);
        Assert.NotEqual(UnavailableReason.EngineMissing, reg.Find("vid-trim")!.Availability.Reason);
    }

    [Fact]
    public void Every_refinement_names_a_real_catalog_tool_and_a_listed_engine()
    {
        var ids = ToolRegistry.BuildCatalog().Select(e => e.Id).ToHashSet();
        foreach (var (id, engine) in Engines.Refinements)
        {
            Assert.Contains(id, ids);
            Assert.Contains(engine, Engines.All);
        }
    }

    // ---- cache and parallelism ---------------------------------------------------------------------

    [Fact]
    public void Each_engine_is_probed_once_per_process_across_all_and_find()
    {
        var probe = FuncProbe.AlwaysPresent();
        var reg = ToolRegistry.Load(probe: probe);
        reg.All();
        var after = probe.Calls;
        reg.All();
        reg.Find("pdf-ocr");
        reg.Find("vid-tts");
        Assert.Equal(after, probe.Calls);
        var names = new HashSet<string>();
        foreach (var t in reg.Entries) names.Add(Engines.EffectiveFor(t).Name);
        Assert.True(after <= names.Count, $"{after} probes for {names.Count} engines");
    }

    [Fact]
    public void List_tools_with_slow_probes_runs_in_parallel_not_in_sequence()
    {
        const int delayMs = 150;
        var slow = new FuncProbe(_ => { Thread.Sleep(delayMs); return (true, ""); });
        var reg = ToolRegistry.Load(probe: slow);

        // Before: the old registry probed one engine at a time under one lock = sum of the delays.
        var engines = ToolRegistry.BuildCatalog()
            .Where(e => e.Verdict is not (ToolVerdict.CloudOnly or ToolVerdict.AiEgress))
            .Select(Engines.EffectiveFor).Where(LicenseGate.IsCleared)
            .GroupBy(e => e.Name + "|" + e.ProbeKey).Select(g => g.First()).ToList();
        var before = Stopwatch.StartNew();
        var serial = new FuncProbe(_ => { Thread.Sleep(delayMs); return (true, ""); });
        foreach (var e in engines) serial.Probe(e);
        before.Stop();

        var sw = Stopwatch.StartNew();
        var (code, _, _) = Cli(reg, "--list-tools");
        sw.Stop();

        _out.WriteLine($"--list-tools with a {delayMs} ms probe over {engines.Count} engines: before (sequential) {before.ElapsedMilliseconds} ms, after (parallel, bound {ToolRegistry.ProbeParallelism}) {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0, code);
        Assert.Equal(engines.Count, slow.Calls);
        Assert.True(sw.ElapsedMilliseconds * 2 <= before.ElapsedMilliseconds,
            $"parallel {sw.ElapsedMilliseconds} ms vs sequential {before.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Probe_parallelism_is_bounded()
    {
        int running = 0, peak = 0;
        var probe = new FuncProbe(_ =>
        {
            var n = Interlocked.Increment(ref running);
            int p; while (n > (p = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, n, p) != p) { }
            Thread.Sleep(60);
            Interlocked.Decrement(ref running);
            return (true, "");
        });
        ToolRegistry.Load(probe: probe).All();
        Assert.InRange(peak, 2, ToolRegistry.ProbeParallelism);
    }

    // ---- doctor ------------------------------------------------------------------------------------

    private static (int Code, string Out, string Err) Cli(ToolRegistry reg, params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = ToolCli.RunWith(args, o, e, reg);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Doctor_prints_one_line_per_engine_and_writes_json()
    {
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "doctor.json");
        var reg = ToolRegistry.Load(probe: Missing("Windows OCR language pack", "FFmpeg"));
        var (code, stdout, _) = Cli(reg, "--doctor", "--json", json);

        Assert.Equal(0, code);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        var engines = doc.RootElement.GetProperty("engines");
        Assert.Equal(lines.Length, engines.GetArrayLength());
        Assert.Contains(lines, l => l.StartsWith("Windows OCR language pack\t") && l.Contains("missing: engine missing: Windows OCR language pack") && l.Contains("1 tools"));
        Assert.Contains(lines, l => l.StartsWith("PdfPig\t") && l.Contains("\tpresent\t"));
        foreach (var e in engines.EnumerateArray())
        {
            Assert.True(e.GetProperty("present").ValueKind is JsonValueKind.True or JsonValueKind.False);
            if (!e.GetProperty("present").GetBoolean()) Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("reason").GetString()));
            Assert.True(e.GetProperty("runners").GetInt32() <= e.GetProperty("tools").GetInt32());
        }
    }

    [Fact]
    public void Doctor_counts_tools_and_runners_per_engine()
    {
        var reg = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent());
        var report = ToolDoctor.Build(reg);
        var ocr = report.Single(r => r.Name == "Windows OCR language pack");
        Assert.Equal(new[] { "pdf-ocr" }, ocr.ToolIds);
        var agpl = report.Where(r => r.License == EngineLicense.AGPL).ToList();
        Assert.Equal(4, agpl.Count);
        Assert.All(agpl, r => { Assert.False(r.Present); Assert.StartsWith("licence not cleared for shipping: ", r.Reason); });
        Assert.Equal(reg.Entries.Count, report.Sum(r => r.ToolCount));
        Assert.Equal(reg.Entries.Count(t => reg.HasRunner(t.Id)), report.Sum(r => r.RunnerCount));
    }

    [Fact]
    public void Every_missing_engine_reason_is_the_reason_each_of_its_tools_shows_in_list_tools()
    {
        var reg = ToolRegistry.Load(probe: Missing("Windows speech voice", "rembg", "WebView2 runtime", "FFmpeg", "Word COM"));
        var report = ToolDoctor.Build(reg);
        var byId = reg.All().ToDictionary(t => t.Entry.Id);
        var checkedTools = 0;
        foreach (var r in report.Where(r => !r.Present))
            foreach (var id in r.ToolIds)
            {
                var a = byId[id].Availability;
                Assert.False(a.Available);
                if (a.Reason is UnavailableReason.CloudOnly or UnavailableReason.NeedsDisclose) continue;
                Assert.Equal(r.Reason, a.Text);
                checkedTools++;
            }
        Assert.True(checkedTools > 10);
    }

    [Theory]
    [InlineData("--doctor", "--tool", "pdf-ocr")]
    [InlineData("--doctor", "--list-tools")]
    [InlineData("--doctor", "--out", "x")]
    [InlineData("--doctor", "--json")]
    [InlineData("--doctor", "--bogus")]
    public void Doctor_with_bad_arguments_exits_2(params string[] args)
    {
        var reg = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent());
        var (code, stdout, err) = Cli(reg, args);
        Assert.Equal(2, code);
        Assert.Equal("", stdout);
        Assert.Contains("bad arguments", err);
    }

    [Fact]
    public void List_tools_shows_the_engine_a_tool_is_judged_on()
    {
        var reg = ToolRegistry.Load(probe: Missing("Windows OCR language pack"));
        var (_, stdout, _) = Cli(reg, "--list-tools");
        var line = stdout.Split('\n').Single(l => l.StartsWith("pdf-ocr\t"));
        Assert.Contains("\tWindows OCR language pack\t", line);
        Assert.EndsWith("unavailable\tengine missing: Windows OCR language pack", line.TrimEnd('\r'));
    }
}
