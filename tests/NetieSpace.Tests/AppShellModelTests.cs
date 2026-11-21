using NetieSpace.Apps.Shell;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

public class RecentFilesTests
{
    private static string Touch(string dir, string name)
    {
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Add_puts_newest_first_and_persists()
    {
        var dir = ToolTestSupport.NewTempDir();
        var a = Touch(dir, "a.pdf");
        var b = Touch(dir, "b.pdf");
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rf = new RecentFiles(IoPath.Combine(dir, "recent.json"), clock: () => t = t.AddMinutes(1));

        rf.Add(a);
        rf.Add(b);

        var reloaded = new RecentFiles(rf.JsonPath).Load();
        Assert.Equal(new[] { b, a }, reloaded.Select(r => r.Path));
    }

    [Fact]
    public void Add_dedupes_by_path_ignoring_case_and_moves_to_front()
    {
        var dir = ToolTestSupport.NewTempDir();
        var a = Touch(dir, "a.pdf");
        var b = Touch(dir, "b.pdf");
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rf = new RecentFiles(IoPath.Combine(dir, "recent.json"), clock: () => t = t.AddMinutes(1));

        rf.Add(a);
        rf.Add(b);
        var rows = rf.Add(a);

        Assert.Equal(2, rows.Count);
        Assert.Equal(a, rows[0].Path);
    }

    [Fact]
    public void Load_prunes_files_that_no_longer_exist()
    {
        var dir = ToolTestSupport.NewTempDir();
        var a = Touch(dir, "a.pdf");
        var b = Touch(dir, "b.pdf");
        var rf = new RecentFiles(IoPath.Combine(dir, "recent.json"));
        rf.Add(a);
        rf.Add(b);

        IoFile.Delete(a);

        Assert.Equal(new[] { b }, rf.Load().Select(r => r.Path));
    }

    [Fact]
    public void Add_caps_the_list_keeping_the_newest()
    {
        var dir = ToolTestSupport.NewTempDir();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rf = new RecentFiles(IoPath.Combine(dir, "recent.json"), cap: 3, clock: () => t = t.AddMinutes(1));
        var paths = Enumerable.Range(0, 5).Select(i => Touch(dir, $"f{i}.pdf")).ToList();

        foreach (var p in paths)
            rf.Add(p);

        Assert.Equal(new[] { paths[4], paths[3], paths[2] }, rf.Load().Select(r => r.Path));
    }

    [Fact]
    public void Missing_or_damaged_file_loads_as_empty()
    {
        var dir = ToolTestSupport.NewTempDir();
        var json = IoPath.Combine(dir, "recent.json");
        Assert.Empty(new RecentFiles(json).Load());

        IoFile.WriteAllText(json, "{ not json");
        Assert.Empty(new RecentFiles(json).Load());
    }

    [Fact]
    public void Default_path_is_per_app_recent_json()
    {
        var p = RecentFiles.DefaultPath(AppIdentity.Image);
        Assert.EndsWith(IoPath.Combine("NetieSpace", "apps", "image", "recent.json"), p);
    }
}

public class ToolPageModelTests
{
    private static readonly EngineRef Eng = new("test", EngineLicense.MIT, true);

    private static RegisteredTool Tool(
        ToolOption[]? options = null, string accepted = ".pdf", int min = 1, int max = 1,
        bool available = true, string reason = "", InstallOffer? offer = null, string verb = "Do it")
    {
        var entry = new ToolEntry(
            "t-1", ToolApp.Pdf, "Cat", "Thing", verb, "thing",
            accepted.Length == 0 ? Array.Empty<string>() : accepted.Split(','),
            min, max, options ?? Array.Empty<ToolOption>(), Eng, ToolVerdict.Local, offer);
        ToolRunnerDelegate run = (_, _, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var avail = available
            ? ToolAvailability.Yes
            : new ToolAvailability(false, UnavailableReason.EngineMissing, reason);
        return new RegisteredTool(entry, run, avail);
    }

    private static string TempFile(string name)
    {
        var p = IoPath.Combine(ToolTestSupport.NewTempDir(), name);
        IoFile.WriteAllText(p, "x");
        return p;
    }

    [Theory]
    [InlineData(ToolOptionKind.Choice, OptionControl.ComboBox)]
    [InlineData(ToolOptionKind.Int, OptionControl.NumberBox)]
    [InlineData(ToolOptionKind.Percent, OptionControl.NumberBox)]
    [InlineData(ToolOptionKind.Bool, OptionControl.CheckBox)]
    [InlineData(ToolOptionKind.Text, OptionControl.TextBox)]
    [InlineData(ToolOptionKind.PageRange, OptionControl.TextBox)]
    [InlineData(ToolOptionKind.Color, OptionControl.TextBox)]
    [InlineData(ToolOptionKind.File, OptionControl.FileBrowse)]
    [InlineData(ToolOptionKind.Grid, OptionControl.GridCombo)]
    public void Each_option_kind_maps_to_its_control(ToolOptionKind kind, OptionControl expected)
    {
        var m = new ToolPageModel(Tool(new[] { new ToolOption("k", kind, "Label", null, new[] { "a", "b" }, 1, 9) }));
        var d = Assert.Single(m.Options);
        Assert.Equal(expected, d.Control);
        Assert.Equal("k", d.Key);
        Assert.Equal("Label", d.Label);
    }

    [Fact]
    public void Descriptor_carries_default_choices_and_range()
    {
        var m = new ToolPageModel(Tool(new[]
        {
            new ToolOption("level", ToolOptionKind.Choice, "Compression", "recommended", new[] { "extreme", "recommended", "low" }),
            new ToolOption("start", ToolOptionKind.Int, "Starting number", "1", null, 0, 99),
            new ToolOption("opacity", ToolOptionKind.Percent, "Opacity", "50"),
            new ToolOption("keep", ToolOptionKind.Bool, "Keep ratio", null),
        }));

        Assert.Equal("recommended", m.Options[0].Default);
        Assert.Equal(new[] { "extreme", "recommended", "low" }, m.Options[0].Choices);
        Assert.Equal((0, 99), (m.Options[1].Min, m.Options[1].Max));
        Assert.Equal((0, 100), (m.Options[2].Min, m.Options[2].Max));
        Assert.Equal("false", m.Options[3].Default);
    }

    [Fact]
    public void Grid_offers_nine_positions_and_all_pass_the_runner()
    {
        var m = new ToolPageModel(Tool(new[] { new ToolOption("pos", ToolOptionKind.Grid, "Position", "middle-center") }));
        var d = m.Options[0];
        Assert.Equal(9, d.Choices.Count);
        var file = TempFile("a.pdf");
        foreach (var c in d.Choices)
        {
            var req = m.BuildRequest(new[] { file }, "out", new Dictionary<string, string> { ["pos"] = c });
            Assert.Null(ToolRunner.Validate(m.Tool.Entry, req, out _));
        }
    }

    [Fact]
    public void Action_label_is_the_entry_verb_verbatim()
    {
        Assert.Equal("Extract", new ToolPageModel(Tool(verb: "Extract")).ActionLabel);
    }

    [Fact]
    public void Unavailable_tool_has_reason_and_a_disabled_action()
    {
        var m = new ToolPageModel(Tool(available: false, reason: "engine missing: ghostscript"));
        var file = TempFile("a.pdf");

        Assert.False(m.Available);
        Assert.Equal("engine missing: ghostscript", m.UnavailableLine);
        Assert.False(m.ActionEnabled(new[] { file }));
    }

    [Fact]
    public void Available_tool_has_no_reason_and_enables_with_a_valid_file()
    {
        var m = new ToolPageModel(Tool());
        var file = TempFile("a.pdf");

        Assert.Null(m.UnavailableLine);
        Assert.True(m.ActionEnabled(new[] { file }));
        Assert.False(m.ActionEnabled(Array.Empty<string>()));
    }

    [Fact]
    public void Install_offer_line_shows_source_and_size_only_when_unavailable()
    {
        var offer = new InstallOffer("https://example.test/gs.zip", 12_582_912, "AGPL");
        var unavailable = new ToolPageModel(Tool(available: false, reason: "engine missing: gs", offer: offer));
        var available = new ToolPageModel(Tool(offer: offer));
        var noOffer = new ToolPageModel(Tool(available: false, reason: "x"));

        Assert.Equal("https://example.test/gs.zip (12 MB)", unavailable.InstallOfferLine);
        Assert.Null(available.InstallOfferLine);
        Assert.Null(noOffer.InstallOfferLine);
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(1_572_864L, "1.5 MB")]
    [InlineData(3_221_225_472L, "3 GB")]
    public void Size_is_formatted(long bytes, string expected) =>
        Assert.Equal(expected, ToolPageModel.FormatSize(bytes));

    [Fact]
    public void File_validation_enforces_min_max_and_accepted_types()
    {
        var m = new ToolPageModel(Tool(min: 2, max: 2));
        var a = TempFile("a.pdf");
        var b = TempFile("b.pdf");
        var c = TempFile("c.pdf");
        var png = TempFile("d.png");

        Assert.False(m.ValidateFiles(new[] { a }).Ok);
        Assert.True(m.ValidateFiles(new[] { a, b }).Ok);
        Assert.False(m.ValidateFiles(new[] { a, b, c }).Ok);
        var wrongType = m.ValidateFiles(new[] { a, png });
        Assert.False(wrongType.Ok);
        Assert.Contains("d.png", wrongType.Message);
    }

    [Fact]
    public void File_validation_message_is_the_runners_own()
    {
        var m = new ToolPageModel(Tool(min: 1, max: 1));
        var a = TempFile("a.pdf");
        var b = TempFile("b.pdf");
        var viaRunner = ToolRunner.Validate(m.Tool.Entry, new ToolRequest(new[] { a, b }, "out", new Dictionary<string, string>()), out _);

        Assert.Equal(viaRunner, m.ValidateFiles(new[] { a, b }).Message);
    }

    [Fact]
    public void Browse_filter_lists_accepted_types_or_all_files()
    {
        Assert.Equal("Supported files|*.jpg;*.png", new ToolPageModel(Tool(accepted: ".JPG,.png")).BrowseFilter());
        Assert.Equal("All files|*.*", new ToolPageModel(Tool(accepted: "")).BrowseFilter());
    }

    [Fact]
    public void Accepts_checks_the_extension_case_insensitively()
    {
        var m = new ToolPageModel(Tool(accepted: ".pdf"));
        Assert.True(m.Accepts(@"C:\x\A.PDF"));
        Assert.False(m.Accepts(@"C:\x\a.png"));
        Assert.True(new ToolPageModel(Tool(accepted: "")).Accepts("anything.xyz"));
    }

    [Fact]
    public void AllowMultiple_follows_MaxFiles()
    {
        Assert.False(new ToolPageModel(Tool(max: 1)).AllowMultiple);
        Assert.True(new ToolPageModel(Tool(max: int.MaxValue)).AllowMultiple);
    }

    [Fact]
    public void MergeFiles_appends_and_dedupes_ignoring_case()
    {
        var merged = ToolPageModel.MergeFiles(new[] { @"C:\a.pdf" }, new[] { @"c:\A.PDF", @"C:\b.pdf", "" });
        Assert.Equal(new[] { @"C:\a.pdf", @"C:\b.pdf" }, merged);
    }

    [Fact]
    public void BuildRequest_omits_blank_values_but_keeps_bools()
    {
        var m = new ToolPageModel(Tool(new[]
        {
            new ToolOption("text", ToolOptionKind.Text, "Text", "DRAFT"),
            new ToolOption("keep", ToolOptionKind.Bool, "Keep", "true"),
            new ToolOption("start", ToolOptionKind.Int, "Start", "1", null, 0, null),
        }));

        var req = m.BuildRequest(new[] { "f.pdf" }, "out", new Dictionary<string, string>
        {
            ["text"] = "  ",
            ["keep"] = "false",
            ["start"] = " 7 ",
        });

        Assert.False(req.Options.ContainsKey("text"));
        Assert.Equal("false", req.Options["keep"]);
        Assert.Equal("7", req.Options["start"]);
        Assert.Equal("out", req.OutDir);
    }

    [Fact]
    public void Result_line_for_success_names_the_output()
    {
        var one = new ToolResult(true, ToolOutcome.Ok, "t-1", "e", new[] { @"C:\o\a.txt" }, null, null, 5);
        var many = new ToolResult(true, ToolOutcome.Ok, "t-1", "e", new[] { @"C:\o\a.txt", @"C:\o\b.txt" }, null, null, 5);

        Assert.Equal(@"Done: C:\o\a.txt", ToolPageModel.ResultLine(one));
        Assert.StartsWith("Done: 2 files in ", ToolPageModel.ResultLine(many));
        Assert.Equal(@"C:\o\a.txt", ToolPageModel.PrimaryOutput(one));
    }

    [Fact]
    public void Result_line_for_failure_is_the_runner_message_verbatim()
    {
        var msg = "IOException: the file is locked; try again";
        var fail = new ToolResult(false, ToolOutcome.Failed, "t-1", "e", Array.Empty<string>(), msg, null, 5);

        Assert.Equal(msg, ToolPageModel.ResultLine(fail));
        Assert.Null(ToolPageModel.PrimaryOutput(fail));
    }

    [Fact]
    public void Explorer_arguments_select_the_file()
    {
        Assert.Equal("/select,\"C:\\o\\a b.txt\"", ToolPageModel.ExplorerSelectArguments(@"C:\o\a b.txt"));
    }

    [Fact]
    public void Default_out_dir_is_beside_the_first_input()
    {
        var f = TempFile("a.pdf");
        Assert.Equal(IoPath.GetDirectoryName(f), ToolPageModel.DefaultOutDir(new[] { f }));
        Assert.Equal("", ToolPageModel.DefaultOutDir(Array.Empty<string>()));
    }
}

public class HomeModelTests
{
    private static readonly EngineRef Eng = new("test", EngineLicense.MIT, true);

    private static RegisteredTool T(string id, ToolApp app, string cat, string name, bool ok = true, string reason = "") =>
        new(
            new ToolEntry(id, app, cat, name, "verb of " + id, id, Array.Empty<string>(), 0, 1,
                Array.Empty<ToolOption>(), Eng, ToolVerdict.Local),
            null,
            ok ? ToolAvailability.Yes : new ToolAvailability(false, UnavailableReason.NotBuilt, reason));

    [Fact]
    public void Categories_follow_catalog_order_and_hold_only_this_app()
    {
        var tools = new[]
        {
            T("a", ToolApp.Pdf, "Organize", "Merge"),
            T("x", ToolApp.Image, "Modify", "Crop"),
            T("b", ToolApp.Pdf, "Edit", "Watermark"),
            T("c", ToolApp.Pdf, "Organize", "Split"),
        };

        var cats = HomeModel.Categories(AppIdentity.Pdf, tools);

        Assert.Equal(new[] { "Organize", "Edit" }, cats.Select(c => c.Name));
        Assert.Equal(new[] { "a", "c" }, cats[0].Tiles.Select(t => t.ToolId));
        Assert.Equal(new[] { "b" }, cats[1].Tiles.Select(t => t.ToolId));
        Assert.DoesNotContain(cats.SelectMany(c => c.Tiles), t => t.ToolId == "x");
    }

    [Fact]
    public void Tile_carries_glyph_name_verb_and_availability()
    {
        var ok = HomeModel.Tile(T("a", ToolApp.Pdf, "C", "merge PDF"));
        var no = HomeModel.Tile(T("b", ToolApp.Pdf, "C", "PDF to PDF/A", ok: false, reason: "engine missing: gs"));

        Assert.Equal("M", ok.Glyph);
        Assert.Equal("merge PDF", ok.Name);
        Assert.Equal("verb of a", ok.Verb);
        Assert.True(ok.Available);
        Assert.Null(ok.ReasonLine);
        Assert.False(no.Available);
        Assert.Equal("engine missing: gs", no.ReasonLine);
    }

    [Fact]
    public void Every_entry_of_the_real_catalog_for_the_app_has_a_tile()
    {
        var all = ToolRegistry.BuildCatalog();
        var tools = all.Select(e => new RegisteredTool(e, null, ToolAvailability.Yes)).ToList();

        foreach (var app in AppIdentity.All)
        {
            var toolApp = HomeModel.ToolAppOf(app);
            var expected = all.Count(e => e.App == toolApp);
            var tiles = HomeModel.Categories(app, tools).Sum(c => c.Tiles.Count);
            Assert.True(expected > 0, app.Key);
            Assert.Equal(expected, tiles);
        }
    }

    [Theory]
    [InlineData("pdf", "image", "video")]
    [InlineData("image", "pdf", "video")]
    [InlineData("video", "pdf", "image")]
    public void Family_targets_are_the_two_other_apps_with_exact_arguments(string current, string first, string second)
    {
        var targets = HomeModel.FamilyTargets(AppIdentity.Find(current)!);

        Assert.Equal(2, targets.Count);
        Assert.Equal(new[] { "--app " + first, "--app " + second }, targets.Select(t => t.Arguments));
        Assert.Equal(new[] { first, second }, targets.Select(t => t.App.Key));
        Assert.All(targets, t => Assert.Equal(t.App.DisplayName, t.Label));
    }
}

public class SnapshotArgsTests
{
    [Fact]
    public void No_snapshot_flags_means_not_requested()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf" });
        Assert.False(a.Requested);
        Assert.Null(a.Error);
    }

    [Fact]
    public void Snapshot_with_path_is_requested()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", @"C:\t\home.png" });
        Assert.True(a.Requested);
        Assert.Equal(@"C:\t\home.png", a.OutPath);
        Assert.Null(a.ToolId);
        Assert.Null(a.Error);
    }

    [Fact]
    public void View_is_read_next_to_snapshot()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", "o.png", "--view", "pdf-extract-text" });
        Assert.Equal("pdf-extract-text", a.ToolId);
    }

    [Theory]
    [InlineData("--snapshot")]
    [InlineData("--snapshot", "--view", "x")]
    public void Snapshot_without_a_path_is_an_error(params string[] tail)
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf" }.Concat(tail).ToArray());
        Assert.NotNull(a.Error);
        Assert.Contains("--snapshot", a.Error);
    }

    [Fact]
    public void View_without_snapshot_is_an_error()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--view", "pdf-merge" });
        Assert.False(a.Requested);
        Assert.NotNull(a.Error);
    }

    [Fact]
    public void Unknown_view_id_is_an_error_naming_it()
    {
        var entries = ToolRegistry.BuildCatalog();
        var err = SnapshotArgs.CheckView("nope", AppIdentity.Pdf, entries);

        Assert.NotNull(err);
        Assert.Contains("nope", err);
        Assert.Equal(2, SnapshotArgs.ExitInvalid);
    }

    [Fact]
    public void Known_view_id_passes_and_other_apps_ids_do_not()
    {
        var entries = ToolRegistry.BuildCatalog();
        var imageId = entries.First(e => e.App == ToolApp.Image).Id;

        Assert.Null(SnapshotArgs.CheckView("pdf-extract-text", AppIdentity.Pdf, entries));
        Assert.Null(SnapshotArgs.CheckView(null, AppIdentity.Pdf, entries));
        Assert.Contains(imageId, SnapshotArgs.CheckView(imageId, AppIdentity.Pdf, entries));
    }
}
