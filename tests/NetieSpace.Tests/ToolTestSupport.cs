using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Tests;

internal sealed class FuncProbe : IEngineProbe
{
    private readonly Func<EngineRef, (bool, string)> _f;
    private int _calls;
    public int Calls => _calls;
    public FuncProbe(Func<EngineRef, (bool, string)> f) { _f = f; }
    public (bool Ok, string Detail) Probe(EngineRef engine) { System.Threading.Interlocked.Increment(ref _calls); return _f(engine); }
    public static FuncProbe AlwaysPresent() => new(_ => (true, ""));
}

/// <summary>Fake provider; opted out so reflection scans of the test assembly never see it.</summary>
[ToolProviderOptOut]
internal sealed class FakeProvider : IToolProvider
{
    private readonly (string, ToolRunnerDelegate)[] _runners;
    public FakeProvider(params (string, ToolRunnerDelegate)[] runners) { _runners = runners; }
    public ToolApp App => ToolApp.Pdf;
    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners() => _runners;
}

internal static class ToolTestSupport
{
    public const string FakeId = "fake-tool";

    public static ToolEntry FakeEntry(EngineRef? engine = null, int min = 0, int max = 1, params ToolOption[] options) =>
        new(FakeId, ToolApp.Pdf, "test", "Fake", "fake", "fake",
            Array.Empty<string>(), min, max, options, engine ?? Engines.PdfSharpCore, ToolVerdict.Local);

    public static ToolRegistry FakeRegistry(ToolRunnerDelegate run, ToolEntry? entry = null, IEngineProbe? probe = null) =>
        ToolRegistry.Load(
            probe: probe ?? FuncProbe.AlwaysPresent(),
            extraProviders: new IToolProvider[] { new FakeProvider((FakeId, run)) },
            catalogOverride: new[] { entry ?? FakeEntry() },
            scanAssemblies: false);

    public static string NewTempDir()
    {
        var d = IoPath.Combine(IoPath.GetTempPath(), "ns-tools-" + Guid.NewGuid().ToString("N"));
        IoDirectory.CreateDirectory(d);
        return d;
    }

    public static string FixturePdf() => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "Q3_Report.pdf");

    public static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !IoFile.Exists(IoPath.Combine(dir.FullName, "NetieSpace.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root (NetieSpace.sln) not found above " + AppContext.BaseDirectory);
        return IoPath.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
    }
}
