// Real probes on a real Windows runner: each must give a definite answer inside the budget.
// Present or missing are both fine; a timeout, a throw or an empty reason is not.
#if WINDOWS
using System.Diagnostics;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;
using Xunit.Abstractions;

namespace NetieSpace.Tests;

public class ToolProbeWindowsTests
{
    private readonly ITestOutputHelper _out;
    public ToolProbeWindowsTests(ITestOutputHelper output) { _out = output; }

    public static IEnumerable<object[]> Engines_to_probe() =>
        new[]
        {
            Engines.WinRt, Engines.WinRtOcr, Engines.WinRtSpeech, Engines.WebView2,
            Engines.OfficeWord, Engines.OfficeExcel, Engines.OfficePowerPoint,
            Engines.Ffmpeg, Engines.FfmpegVidstab, Engines.FfmpegDrawtext, Engines.FfmpegSubtitles,
        }.Select(e => new object[] { e.Name });

    [Theory]
    [MemberData(nameof(Engines_to_probe))]
    public void Real_probe_returns_a_definite_answer_inside_the_budget(string engineName)
    {
        var engine = Engines.All.Single(e => e.Name == engineName);
        var sw = Stopwatch.StartNew();
        var (ok, detail) = new EngineProbes().Probe(engine);
        sw.Stop();
        _out.WriteLine($"{engineName}: {(ok ? "present" : "missing: " + detail)} in {sw.ElapsedMilliseconds} ms");
        Assert.DoesNotContain("timed out", detail);
        if (!ok) Assert.False(string.IsNullOrWhiteSpace(detail), "a missing engine must say what is missing");
    }

    [Fact]
    public void Ocr_probe_agrees_with_the_Windows_recognizer_list()
    {
        var installed = global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0;
        Assert.Equal(installed, new EngineProbes().Probe(Engines.WinRtOcr).Ok);
    }

    [Fact]
    public void Speech_probe_agrees_with_the_Windows_voice_list()
    {
        var installed = global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Count > 0;
        Assert.Equal(installed, new EngineProbes().Probe(Engines.WinRtSpeech).Ok);
    }

    [Fact]
    public void Doctor_on_the_real_registry_gives_every_engine_a_definite_state()
    {
        var report = ToolDoctor.Build(ToolRegistry.Load());
        foreach (var r in report) _out.WriteLine($"{r.Name}: {(r.Present ? "present" : r.Reason)} tools={r.ToolCount} runners={r.RunnerCount}");
        Assert.All(report, r => Assert.True(r.Present || !string.IsNullOrWhiteSpace(r.Reason)));
        Assert.All(report, r => Assert.DoesNotContain("timed out", r.Reason));
    }
}
#endif
