using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Tests;

public class ToolCliTests
{
    private static readonly Regex ClosedReasons = new(
        @"^(not built in this version|engine missing: .+|licence not cleared for shipping: .+|needs Space#3 disclose|cloud only)$");

    private static (int Exit, string Out, string Err) Cli(ToolRegistry reg, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = ToolCli.RunWith(args, o, e, reg);
        return (code, o.ToString(), e.ToString());
    }

    private static ToolRegistry Real() => ToolRegistry.Load(probe: new EngineProbes());

    [Fact]
    public void Public_entry_point_has_the_exact_reflection_signature()
    {
        var m = typeof(ToolCli).GetMethod("Run", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string[]) });
        Assert.NotNull(m);
        Assert.Equal(typeof(int), m!.ReturnType);
        Assert.Equal("NetieSpace.Apps.Tools.ToolCli", typeof(ToolCli).FullName);
    }

    [Fact]
    public void List_tools_prints_91_lines_split_37_16_38_with_closed_set_reasons_and_matching_json()
    {
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "list.json");
        var (exit, stdout, _) = Cli(Real(), "--list-tools", "--json", json);

        Assert.Equal(0, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(91, lines.Length);
        var cols = lines.Select(l => l.Split('\t')).ToArray();
        Assert.All(cols, c => Assert.Equal(6, c.Length));
        Assert.Equal(37, cols.Count(c => c[1] == "pdf"));
        Assert.Equal(16, cols.Count(c => c[1] == "image"));
        Assert.Equal(38, cols.Count(c => c[1] == "video"));
        foreach (var c in cols)
        {
            Assert.Contains(c[4], new[] { "available", "unavailable" });
            if (c[4] == "unavailable") Assert.Matches(ClosedReasons, c[5]);
            else Assert.Equal("", c[5]);
        }
        Assert.Contains(cols, c => c[0] == "pdf-extract-text" && c[4] == "available");

        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(91, tools.GetArrayLength());
        Assert.Equal(91, doc.RootElement.GetProperty("count").GetInt32());
        var first = tools[0];
        foreach (var p in new[] { "id", "app", "engine", "licence", "available" })
            Assert.True(first.TryGetProperty(p, out _), p);
    }

    [Fact]
    public void Extract_text_runs_end_to_end_and_the_json_lists_the_txt_with_ok_true()
    {
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out");
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "r.json");

        var (exit, _, err) = Cli(Real(), "--tool", "pdf-extract-text", "--in", ToolTestSupport.FixturePdf(), "--out", outDir, "--json", json);

        Assert.True(exit == 0, err);
        var txt = IoPath.Combine(outDir, "Q3_Report.txt");
        Assert.True(IoFile.Exists(txt));
        Assert.True(new FileInfo(txt).Length > 0);
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(txt, doc.RootElement.GetProperty("outputs")[0].GetString());
    }

    [Fact]
    public void Unavailable_tool_exits_2_runs_nothing_and_writes_ok_false_with_the_reason()
    {
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out2");
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "r2.json");

        var (exit, _, err) = Cli(Real(), "--tool", "pdf-edit", "--in", ToolTestSupport.FixturePdf(),
            "--out", outDir, "--opt", "text=DRAFT", "--json", json);

        Assert.Equal(2, exit);
        Assert.Contains("not built in this version", err);
        Assert.False(IoDirectory.Exists(outDir));
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("not built in this version", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Agpl_engine_entry_exits_2_with_licence_reason_even_when_every_probe_says_present()
    {
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out3");
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "r3.json");
        var reg = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent());

        var (exit, _, _) = Cli(reg, "--tool", "pdf-to-pdfa", "--in", ToolTestSupport.FixturePdf(), "--out", outDir, "--json", json);

        Assert.Equal(2, exit);
        Assert.False(IoDirectory.Exists(outDir));
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        Assert.Equal("licence not cleared for shipping: Ghostscript", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Lying_runner_exits_1_with_ok_false_naming_the_missing_path()
    {
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out4");
        var ghost = IoPath.Combine(outDir, "ghost.bin");
        var json = IoPath.Combine(ToolTestSupport.NewTempDir(), "r4.json");
        var reg = ToolTestSupport.FakeRegistry((_, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { ghost }));

        var (exit, _, err) = Cli(reg, "--tool", ToolTestSupport.FakeId, "--out", outDir, "--json", json);

        Assert.Equal(1, exit);
        Assert.Contains(ghost, err);
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(json));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(ghost, doc.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--tool")]
    [InlineData("--list-tools", "--tool", "pdf-merge")]
    public void Bad_arguments_exit_2_and_say_why_on_stderr(params string[] args)
    {
        var (exit, _, err) = Cli(Real(), args);
        Assert.Equal(2, exit);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void No_arguments_and_unknown_tool_and_missing_out_exit_2()
    {
        Assert.Equal(2, Cli(Real()).Exit);
        var (e1, _, err1) = Cli(Real(), "--tool", "pdf-nope", "--out", ToolTestSupport.NewTempDir());
        Assert.Equal(2, e1);
        Assert.Contains("pdf-nope", err1);
        Assert.Equal(2, Cli(Real(), "--tool", "pdf-extract-text", "--in", ToolTestSupport.FixturePdf()).Exit);
    }

    [Fact]
    public void Bad_option_value_exits_2_naming_the_key_and_nothing_is_written()
    {
        var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out5");
        var (exit, _, err) = Cli(Real(), "--tool", "pdf-extract-text", "--in", ToolTestSupport.FixturePdf(),
            "--out", outDir, "--opt", "detailed=maybe");
        Assert.Equal(2, exit);
        Assert.Contains("detailed", err);
        Assert.False(IoDirectory.Exists(outDir));
    }
}
