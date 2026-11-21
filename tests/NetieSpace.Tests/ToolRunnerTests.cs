using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Tests;

public class ToolRunnerTests
{
    private static Task<ToolResult> Run(ToolRegistry reg, ToolRequest req, CancellationToken ct = default) =>
        ToolRunner.RunAsync(reg.Find(ToolTestSupport.FakeId)!, req, null, ct);

    private static ToolRequest Req(string outDir, params string[] files) =>
        new(files, outDir, new Dictionary<string, string>());

    [Fact]
    public async Task Runner_claiming_a_missing_output_yields_ok_false_naming_the_path()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var ghost = IoPath.Combine(outDir, "ghost.txt");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { ghost }));

        var r = await Run(reg, Req(outDir));

        Assert.False(r.Ok);
        Assert.Equal(ToolOutcome.Failed, r.Outcome);
        Assert.Contains(ghost, r.Error);
        Assert.Contains("missing", r.Error);
    }

    [Fact]
    public async Task Runner_claiming_a_zero_byte_output_yields_ok_false_naming_the_path()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var empty = IoPath.Combine(outDir, "empty.txt");
        var reg = ToolTestSupport.FakeRegistry((req, _, _) =>
        {
            IoFile.WriteAllBytes(empty, Array.Empty<byte>());
            return Task.FromResult<IReadOnlyList<string>>(new[] { empty });
        });

        var r = await Run(reg, Req(outDir));

        Assert.False(r.Ok);
        Assert.Contains(empty, r.Error);
        Assert.Contains("zero bytes", r.Error);
    }

    [Fact]
    public async Task One_good_output_and_one_lying_output_still_fails()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var good = IoPath.Combine(outDir, "good.txt");
        var ghost = IoPath.Combine(outDir, "ghost.txt");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) =>
        {
            IoFile.WriteAllText(good, "x");
            return Task.FromResult<IReadOnlyList<string>>(new[] { good, ghost });
        });

        var r = await Run(reg, Req(outDir));

        Assert.False(r.Ok);
        Assert.Contains(ghost, r.Error);
    }

    [Fact]
    public async Task Runner_claiming_no_outputs_at_all_is_a_failure()
    {
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        var r = await Run(reg, Req(ToolTestSupport.NewTempDir()));
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Honest_runner_gets_ok_true_with_the_output_and_engine_name()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var good = IoPath.Combine(outDir, "good.txt");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) =>
        {
            IoFile.WriteAllText(good, "hello");
            return Task.FromResult<IReadOnlyList<string>>(new[] { good });
        });

        var r = await Run(reg, Req(outDir));

        Assert.True(r.Ok);
        Assert.Equal(new[] { good }, r.Outputs);
        Assert.Equal("PdfSharpCore", r.Engine);
        Assert.True(r.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Runner_exception_text_is_preserved_in_the_result()
    {
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => throw new InvalidOperationException("engine exploded: code 42"));
        var r = await Run(reg, Req(ToolTestSupport.NewTempDir()));
        Assert.False(r.Ok);
        Assert.Contains("engine exploded: code 42", r.Error);
    }

    [Fact]
    public async Task Cancellation_returns_a_failed_result_not_a_throw()
    {
        using var cts = new CancellationTokenSource();
        var reg = ToolTestSupport.FakeRegistry(async (_, _, ct) =>
        {
            cts.Cancel();
            await Task.Delay(5000, ct);
            return Array.Empty<string>();
        });
        var r = await Run(reg, Req(ToolTestSupport.NewTempDir()), cts.Token);
        Assert.False(r.Ok);
        Assert.Equal("cancelled", r.Error);
    }

    [Fact]
    public async Task Unavailable_tool_is_not_run_and_the_out_dir_is_not_created()
    {
        var ran = false;
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "never");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => { ran = true; return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); },
            probe: new FuncProbe(_ => (false, "fake engine")));

        var r = await Run(reg, Req(outDir));

        Assert.False(ran);
        Assert.False(r.Ok);
        Assert.Equal(ToolOutcome.Unavailable, r.Outcome);
        Assert.Equal("engine missing: fake engine", r.Reason);
        Assert.False(IoDirectory.Exists(outDir));
    }

    [Fact]
    public async Task Too_many_files_wrong_type_and_missing_file_are_rejected_before_the_runner_runs()
    {
        var dir = ToolTestSupport.NewTempDir();
        var a = IoPath.Combine(dir, "a.pdf"); IoFile.WriteAllText(a, "x");
        var b = IoPath.Combine(dir, "b.txt"); IoFile.WriteAllText(b, "x");
        var ran = false;
        var entry = ToolTestSupport.FakeEntry(min: 1, max: 1) with { AcceptedTypes = new[] { ".pdf" } };
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => { ran = true; return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); }, entry);

        var tooMany = await Run(reg, Req(dir, a, a));
        var wrongType = await Run(reg, Req(dir, b));
        var missing = await Run(reg, Req(dir, IoPath.Combine(dir, "nope.pdf")));
        var tooFew = await Run(reg, Req(dir));

        Assert.False(ran);
        Assert.All(new[] { tooMany, wrongType, missing, tooFew }, r => { Assert.False(r.Ok); Assert.Equal(ToolOutcome.Invalid, r.Outcome); });
        Assert.Contains("at most", tooMany.Error);
        Assert.Contains(b, wrongType.Error);
        Assert.Contains("nope.pdf", missing.Error);
    }

    [Theory]
    [InlineData("level", "ultra", "level")]
    [InlineData("count", "abc", "count")]
    [InlineData("count", "99", "count")]
    [InlineData("pct", "150", "pct")]
    [InlineData("flag", "yes", "flag")]
    [InlineData("pages", "x-y", "pages")]
    [InlineData("color", "red", "color")]
    [InlineData("unknown_key", "1", "unknown_key")]
    public async Task Bad_option_values_fail_naming_the_key(string key, string value, string named)
    {
        var entry = ToolTestSupport.FakeEntry(null, 0, 1,
            new ToolOption("level", ToolOptionKind.Choice, "L", "low", new[] { "low", "high" }),
            new ToolOption("count", ToolOptionKind.Int, "C", "2", null, 1, 16),
            new ToolOption("pct", ToolOptionKind.Percent, "P", "50"),
            new ToolOption("flag", ToolOptionKind.Bool, "F", "false"),
            new ToolOption("pages", ToolOptionKind.PageRange, "R", null),
            new ToolOption("color", ToolOptionKind.Color, "K", "#000000"));
        var ran = false;
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => { ran = true; return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); }, entry);

        var r = await Run(reg, new ToolRequest(Array.Empty<string>(), ToolTestSupport.NewTempDir(), new Dictionary<string, string> { [key] = value }));

        Assert.False(ran);
        Assert.Equal(ToolOutcome.Invalid, r.Outcome);
        Assert.Contains("'" + named + "'", r.Error);
    }

    [Fact]
    public async Task Valid_options_are_accepted_and_defaults_are_filled_in_for_the_runner()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var path = IoPath.Combine(outDir, "o.txt");
        IReadOnlyDictionary<string, string>? seen = null;
        var entry = ToolTestSupport.FakeEntry(null, 0, 1,
            new ToolOption("level", ToolOptionKind.Choice, "L", "low", new[] { "low", "high" }),
            new ToolOption("pages", ToolOptionKind.PageRange, "R", null));
        var reg = ToolTestSupport.FakeRegistry((req, _, _) =>
        {
            seen = req.Options;
            IoFile.WriteAllText(path, "x");
            return Task.FromResult<IReadOnlyList<string>>(new[] { path });
        }, entry);

        var r = await Run(reg, new ToolRequest(Array.Empty<string>(), outDir, new Dictionary<string, string> { ["pages"] = "1,3,5-9,-2-end" }));

        Assert.True(r.Ok, r.Error);
        Assert.Equal("low", seen!["level"]);
        Assert.Equal("1,3,5-9,-2-end", seen["pages"]);
    }

    [Fact]
    public async Task Real_pdf_extract_text_runner_writes_a_nonempty_txt()
    {
        var reg = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent());
        var outDir = ToolTestSupport.NewTempDir();
        var r = await ToolRunner.RunAsync(reg.Find("pdf-extract-text")!,
            new ToolRequest(new[] { ToolTestSupport.FixturePdf() }, outDir, new Dictionary<string, string>()), null, default);

        Assert.True(r.Ok, r.Error);
        var txt = Assert.Single(r.Outputs);
        Assert.Equal(IoPath.Combine(outDir, "Q3_Report.txt"), txt);
        Assert.True(new FileInfo(txt).Length > 0);
    }
}
