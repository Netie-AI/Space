using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>Review fixes for ToolRunner and its option validation (S50 wave 4). Each test was written red first.</summary>
public class ReviewRunnerTests
{
    private static Task<ToolResult> Run(ToolRegistry reg, ToolRequest req, CancellationToken ct = default) =>
        ToolRunner.RunAsync(reg.Find(ToolTestSupport.FakeId)!, req, null, ct);

    private static ToolRequest Req(string outDir, params string[] files) =>
        new(files, outDir, new Dictionary<string, string>());

    [Fact]
    public async Task A_failing_run_does_not_delete_the_files_of_a_concurrent_run_on_the_same_folder()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var good = IoPath.Combine(outDir, "good.txt");
        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var reg = ToolTestSupport.FakeRegistry(async (_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                aStarted.SetResult();
                await release.Task;
                throw new InvalidOperationException("run A fails late");
            }
            IoFile.WriteAllText(good, "from run B");
            return new[] { good };
        });

        var a = Run(reg, Req(outDir));
        await aStarted.Task;
        var b = Run(reg, Req(outDir));
        await Task.Delay(300);
        release.SetResult();
        var ra = await a;
        var rb = await b;

        Assert.False(ra.Ok);
        Assert.True(rb.Ok, rb.Error);
        Assert.True(IoFile.Exists(good), "run A's cleanup deleted run B's finished output");
        Assert.Equal("from run B", IoFile.ReadAllText(good));
    }

    [Fact]
    public async Task A_run_waiting_for_the_folder_can_still_be_cancelled()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var reg = ToolTestSupport.FakeRegistry(async (_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                aStarted.SetResult();
                await release.Task;
            }
            var p = IoPath.Combine(outDir, "x" + calls + ".txt");
            IoFile.WriteAllText(p, "x");
            return new[] { p };
        });

        var a = Run(reg, Req(outDir));
        await aStarted.Task;
        using var cts = new CancellationTokenSource();
        var b = Run(reg, Req(outDir), cts.Token);
        cts.Cancel();
        var rb = await b.WaitAsync(TimeSpan.FromSeconds(10));
        release.SetResult();
        var ra = await a;

        Assert.False(rb.Ok);
        Assert.Equal("cancelled", rb.Error);
        Assert.True(ra.Ok, ra.Error);
        Assert.Single(IoDirectory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public async Task A_runner_that_claims_an_existing_file_without_writing_it_is_not_ok()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var old = IoPath.Combine(outDir, "old.txt");
        IoFile.WriteAllText(old, "from an earlier run");
        IoFile.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { old }));

        var r = await Run(reg, Req(outDir));

        Assert.False(r.Ok);
        Assert.Equal(ToolOutcome.Failed, r.Outcome);
        Assert.Contains(old, r.Error);
        Assert.True(IoFile.Exists(old), "an untouched earlier file must stay");
        Assert.Equal("from an earlier run", IoFile.ReadAllText(old));
    }

    [Fact]
    public async Task A_runner_that_rewrites_an_existing_file_is_ok()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var old = IoPath.Combine(outDir, "old.txt");
        IoFile.WriteAllText(old, "same size 1");
        IoFile.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
        var reg = ToolTestSupport.FakeRegistry((_, _, _) =>
        {
            IoFile.WriteAllText(old, "same size 2");
            return Task.FromResult<IReadOnlyList<string>>(new[] { old });
        });

        var r = await Run(reg, Req(outDir));

        Assert.True(r.Ok, r.Error);
        Assert.Equal(new[] { old }, r.Outputs);
    }

    [Fact]
    public async Task A_failure_result_lists_only_outputs_that_still_exist()
    {
        var outDir = ToolTestSupport.NewTempDir();
        var kept = IoPath.Combine(outDir, "kept.txt");
        IoFile.WriteAllText(kept, "earlier work");
        IoFile.SetLastWriteTimeUtc(kept, DateTime.UtcNow.AddHours(-3));
        var fresh = IoPath.Combine(outDir, "fresh.txt");
        var ghost = IoPath.Combine(outDir, "ghost.txt");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) =>
        {
            IoFile.WriteAllText(fresh, "half of a batch");
            return Task.FromResult<IReadOnlyList<string>>(new[] { fresh, ghost });
        });

        var r = await Run(reg, Req(outDir));

        Assert.False(r.Ok);
        Assert.Contains(ghost, r.Error);
        Assert.False(IoFile.Exists(fresh), "the failed run cleans up what it wrote");
        Assert.DoesNotContain(fresh, r.Outputs);
        Assert.All(r.Outputs, o => Assert.True(IoFile.Exists(o), o + " is listed but gone"));
    }

    private static ToolEntry PagesEntry() => ToolTestSupport.FakeEntry(null, 0, 1,
        new ToolOption("pages", ToolOptionKind.PageRange, "R", null));

    [Theory]
    [InlineData("0")]
    [InlineData("0-3")]
    [InlineData("5-2")]
    [InlineData("-end")]
    [InlineData("-0")]
    [InlineData("1,3-0")]
    [InlineData("3-0")]
    [InlineData("99999999999999")]
    public void Nonsense_page_ranges_are_invalid_and_name_the_key(string spec)
    {
        var err = ToolRunner.Validate(PagesEntry(),
            new ToolRequest(Array.Empty<string>(), "out", new Dictionary<string, string> { ["pages"] = spec }), out _);
        Assert.NotNull(err);
        Assert.Contains("'pages'", err);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1-3")]
    [InlineData("2-2")]
    [InlineData("end")]
    [InlineData("-1")]
    [InlineData("-2-end")]
    [InlineData("1,3,5-9")]
    [InlineData("3-end")]
    [InlineData("1-end")]
    [InlineData("end-end")]
    [InlineData(" 1 , 2-3 ")]
    public void Sensible_page_ranges_stay_valid(string spec)
    {
        var err = ToolRunner.Validate(PagesEntry(),
            new ToolRequest(Array.Empty<string>(), "out", new Dictionary<string, string> { ["pages"] = spec }), out _);
        Assert.Null(err);
    }

    [Theory]
    [InlineData(ToolOptionKind.Choice)]
    [InlineData(ToolOptionKind.Int)]
    [InlineData(ToolOptionKind.Percent)]
    [InlineData(ToolOptionKind.Bool)]
    [InlineData(ToolOptionKind.Text)]
    [InlineData(ToolOptionKind.PageRange)]
    [InlineData(ToolOptionKind.Color)]
    [InlineData(ToolOptionKind.File)]
    [InlineData(ToolOptionKind.Grid)]
    public async Task A_null_option_value_is_an_invalid_result_not_a_throw(ToolOptionKind kind)
    {
        var entry = ToolTestSupport.FakeEntry(null, 0, 1,
            new ToolOption("opt", kind, "O", null, kind == ToolOptionKind.Choice ? new[] { "a", "b" } : null));
        var ran = false;
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => { ran = true; return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); }, entry);

        var r = await Run(reg, new ToolRequest(Array.Empty<string>(), ToolTestSupport.NewTempDir(),
            new Dictionary<string, string> { ["opt"] = null! }));

        Assert.False(ran);
        Assert.Equal(ToolOutcome.Invalid, r.Outcome);
        Assert.Contains("'opt'", r.Error);
    }

    [Fact]
    public void A_trailing_newline_does_not_pass_color_or_grid()
    {
        var entry = ToolTestSupport.FakeEntry(null, 0, 1,
            new ToolOption("color", ToolOptionKind.Color, "K", "#000000"),
            new ToolOption("grid", ToolOptionKind.Grid, "G", "center"));
        string? Check(string key, string value) =>
            ToolRunner.Validate(entry, new ToolRequest(Array.Empty<string>(), "out", new Dictionary<string, string> { [key] = value }), out _);

        Assert.Null(Check("color", "#aabbcc"));
        Assert.Null(Check("grid", "center"));
        Assert.Contains("'color'", Check("color", "#aabbcc\n"));
        Assert.Contains("'color'", Check("color", "#aabbccdd\n"));
        Assert.Contains("'grid'", Check("grid", "center\n"));
        Assert.Contains("'grid'", Check("grid", "top-left\n"));
    }

    [Fact]
    public async Task Choice_values_are_stored_in_their_canonical_spelling()
    {
        var entry = ToolTestSupport.FakeEntry(null, 0, 1,
            new ToolOption("level", ToolOptionKind.Choice, "L", "low", new[] { "low", "High" }));
        IReadOnlyDictionary<string, string>? seen = null;
        var outDir = ToolTestSupport.NewTempDir();
        var path = IoPath.Combine(outDir, "o.txt");
        var reg = ToolTestSupport.FakeRegistry((req, _, _) =>
        {
            seen = req.Options;
            IoFile.WriteAllText(path, "x");
            return Task.FromResult<IReadOnlyList<string>>(new[] { path });
        }, entry);

        var r = await Run(reg, new ToolRequest(Array.Empty<string>(), outDir, new Dictionary<string, string> { ["level"] = "HIGH" }));

        Assert.True(r.Ok, r.Error);
        Assert.Equal("High", seen!["level"]);
    }
}
