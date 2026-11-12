using System.Text.Json;
using NetieSpace.Apps.Shell;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

public class AppShell2StateTests
{
    private static ToolEntry Entry(string id = "t1") => new(
        id, ToolApp.Pdf, "Edit PDF", "Thing", "Do", "g", new[] { ".pdf" }, 1, 3,
        new[]
        {
            new ToolOption("mode", ToolOptionKind.Choice, "Mode", "a", new[] { "a", "b" }),
            new ToolOption("dpi", ToolOptionKind.Int, "Dots", "150", null, 36, 600),
            new ToolOption("opacity", ToolOptionKind.Percent, "Opacity", "50"),
            new ToolOption("tile", ToolOptionKind.Bool, "Tile", "false"),
            new ToolOption("color", ToolOptionKind.Color, "Colour", "#000000"),
            new ToolOption("pages", ToolOptionKind.PageRange, "Pages", "1-end"),
            new ToolOption("text", ToolOptionKind.Text, "Text", "DRAFT"),
            new ToolOption("image", ToolOptionKind.File, "Picture", null),
            new ToolOption("position", ToolOptionKind.Grid, "Position", "middle-center"),
            new ToolOption("password", ToolOptionKind.Text, "Password", null),
        },
        new EngineRef("e", EngineLicense.MIT, true), ToolVerdict.Local);

    private static (ToolStateStore Store, string Dir) NewStore()
    {
        var dir = ToolTestSupport.NewTempDir();
        return (new ToolStateStore(IoPath.Combine(dir, "apps", "pdf", "tool-state.json")), dir);
    }

    [Fact]
    public void Saved_values_and_folder_come_back_from_a_fresh_store()
    {
        var (store, dir) = NewStore();
        var out1 = IoPath.Combine(dir, "out");
        IoDirectory.CreateDirectory(out1);
        var pic = IoPath.Combine(dir, "logo.png");
        IoFile.WriteAllText(pic, "x");

        var values = new Dictionary<string, string>
        {
            ["mode"] = "B", ["dpi"] = "300", ["opacity"] = "70", ["tile"] = "true", ["color"] = "#FF8800",
            ["pages"] = "1-3,5", ["text"] = "SECRET DRAFT", ["image"] = pic, ["position"] = "top-left",
        };
        Assert.True(store.Save(Entry(), values, out1));

        // Read the file itself, not the store, to see what a person's disk holds.
        using var doc = JsonDocument.Parse(IoFile.ReadAllText(store.JsonPath));
        var row = doc.RootElement.GetProperty("t1");
        Assert.Equal(out1, row.GetProperty("outDir").GetString());
        Assert.Equal("b", row.GetProperty("options").GetProperty("mode").GetString()); // catalog spelling

        var got = new ToolStateStore(store.JsonPath).Load(Entry());
        Assert.Equal(out1, got.OutDir);
        Assert.Equal("b", got.Options["mode"]);
        Assert.Equal("300", got.Options["dpi"]);
        Assert.Equal("true", got.Options["tile"]);
        Assert.Equal("#FF8800", got.Options["color"]);
        Assert.Equal("1-3,5", got.Options["pages"]);
        Assert.Equal("SECRET DRAFT", got.Options["text"]);
        Assert.Equal(pic, got.Options["image"]);
        Assert.Equal("top-left", got.Options["position"]);
    }

    [Fact]
    public void Passwords_are_never_written_to_disk()
    {
        var (store, _) = NewStore();
        store.Save(Entry(), new Dictionary<string, string> { ["password"] = "hunter2", ["dpi"] = "100" }, null);
        var text = IoFile.ReadAllText(store.JsonPath);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("password", text);
        Assert.Equal("100", store.Load(Entry()).Options["dpi"]);
    }

    [Theory]
    [InlineData("not json at all {{{")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{\"t1\": 5}")]
    [InlineData("{\"t1\": {\"options\": [1], \"outDir\": 7}}")]
    public void Corrupt_files_read_as_empty_and_never_throw(string content)
    {
        var (store, _) = NewStore();
        IoDirectory.CreateDirectory(IoPath.GetDirectoryName(store.JsonPath)!);
        IoFile.WriteAllText(store.JsonPath, content);
        var got = store.Load(Entry());
        Assert.Empty(got.Options);
        Assert.Null(got.OutDir);
        // And a save over the damage works.
        Assert.True(store.Save(Entry(), new Dictionary<string, string> { ["dpi"] = "90" }, null));
        Assert.Equal("90", store.Load(Entry()).Options["dpi"]);
    }

    [Fact]
    public void Missing_file_reads_as_empty()
    {
        var (store, _) = NewStore();
        Assert.Same(ToolMemory.Empty, store.Load(Entry()));
    }

    [Fact]
    public void Stale_keys_and_values_no_longer_valid_are_dropped_on_load()
    {
        var (store, dir) = NewStore();
        IoDirectory.CreateDirectory(IoPath.GetDirectoryName(store.JsonPath)!);
        IoFile.WriteAllText(store.JsonPath, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["t1"] = new Dictionary<string, object>
            {
                ["options"] = new Dictionary<string, string>
                {
                    ["removed_option"] = "x",          // key the entry no longer has
                    ["mode"] = "zzz",                  // choice no longer offered
                    ["dpi"] = "9999",                  // above max
                    ["opacity"] = "101",               // outside percent range
                    ["tile"] = "maybe",
                    ["color"] = "red",
                    ["pages"] = "1-3; drop table",
                    ["image"] = IoPath.Combine(dir, "gone.png"), // file deleted since
                    ["position"] = "sideways",
                    ["text"] = "ok",
                },
                ["outDir"] = IoPath.Combine(dir, "deleted-folder"),
            },
        }));

        var got = store.Load(Entry());
        Assert.Equal(new[] { "text" }, got.Options.Keys.ToArray());
        Assert.Null(got.OutDir);
    }

    [Fact]
    public void Saving_one_tool_keeps_the_others_and_replaces_its_own_row()
    {
        var (store, _) = NewStore();
        store.Save(Entry("t1"), new Dictionary<string, string> { ["dpi"] = "100" }, null);
        store.Save(Entry("t2"), new Dictionary<string, string> { ["dpi"] = "200" }, null);
        store.Save(Entry("t1"), new Dictionary<string, string> { ["text"] = "hi" }, null);

        var t1 = store.Load(Entry("t1"));
        Assert.Equal("hi", t1.Options["text"]);
        Assert.False(t1.Options.ContainsKey("dpi"));
        Assert.Equal("200", store.Load(Entry("t2")).Options["dpi"]);
    }

    [Fact]
    public void An_unwritable_location_returns_false_instead_of_throwing()
    {
        var dir = ToolTestSupport.NewTempDir();
        var blocker = IoPath.Combine(dir, "file");
        IoFile.WriteAllText(blocker, "x");
        var store = new ToolStateStore(IoPath.Combine(blocker, "sub", "tool-state.json")); // parent is a file
        Assert.False(store.Save(Entry(), new Dictionary<string, string> { ["dpi"] = "1" }, null));
        Assert.Same(ToolMemory.Empty, store.Load(Entry()));
    }

    [Fact]
    public void Default_path_is_under_localappdata_per_app()
    {
        var p = ToolStateStore.DefaultPath(AppIdentity.Video);
        Assert.EndsWith(IoPath.Combine("NetieSpace", "apps", "video", "tool-state.json"), p);
    }
}

public class AppShell2FolderTests
{
    [Fact]
    public void Default_is_a_netie_app_folder_beside_the_first_input()
    {
        var dir = ToolTestSupport.NewTempDir();
        var f = IoPath.Combine(dir, "a.pdf");
        Assert.Equal(IoPath.Combine(dir, "Netie PDF"), OutputFolder.Default(AppIdentity.Pdf, new[] { f, "other.pdf" }));
        Assert.Equal(IoPath.Combine(dir, "Netie Image"), OutputFolder.Default(AppIdentity.Image, new[] { f }));
        Assert.Equal("", OutputFolder.Default(AppIdentity.Pdf, Array.Empty<string>()));
    }

    [Fact]
    public void A_chosen_folder_wins_over_the_default()
    {
        var f = IoPath.Combine(ToolTestSupport.NewTempDir(), "a.pdf");
        Assert.Equal("D:\\Out", OutputFolder.Resolve(AppIdentity.Pdf, new[] { f }, "D:\\Out"));
        Assert.Equal(OutputFolder.Default(AppIdentity.Pdf, new[] { f }), OutputFolder.Resolve(AppIdentity.Pdf, new[] { f }, "  "));
    }

    [Fact]
    public void Prepare_creates_the_folder_and_leaves_no_probe_behind()
    {
        var target = IoPath.Combine(ToolTestSupport.NewTempDir(), "Netie PDF");
        var r = OutputFolder.Prepare(target);
        Assert.True(r.Ok, r.Message);
        Assert.True(IoDirectory.Exists(target));
        Assert.Empty(IoDirectory.GetFileSystemEntries(target));
    }

    [Fact]
    public void Prepare_refuses_a_path_that_is_a_file_naming_it()
    {
        var file = IoPath.Combine(ToolTestSupport.NewTempDir(), "taken");
        IoFile.WriteAllText(file, "x");
        var r = OutputFolder.Prepare(file);
        Assert.False(r.Ok);
        Assert.Contains(file, r.Message);
    }

    [Fact]
    public void Prepare_refuses_a_folder_that_cannot_be_created_under_a_file()
    {
        var file = IoPath.Combine(ToolTestSupport.NewTempDir(), "taken");
        IoFile.WriteAllText(file, "x");
        var r = OutputFolder.Prepare(IoPath.Combine(file, "inside"));
        Assert.False(r.Ok);
        Assert.Contains("inside", r.Message);
    }

    [Fact]
    public void Prepare_refuses_blank_and_invalid_text()
    {
        Assert.False(OutputFolder.Prepare(null).Ok);
        Assert.False(OutputFolder.Prepare("   ").Ok);
        Assert.False(OutputFolder.Prepare("bad\0name").Ok);
    }
}

public class AppShell2QueueTests
{
    private static ToolResult Ok(params string[] outs) =>
        new(true, ToolOutcome.Ok, "t", "e", outs, null, null, 1);

    private static ToolResult Fail(string msg) =>
        new(false, ToolOutcome.Failed, "t", "e", Array.Empty<string>(), msg, null, 1);

    [Fact]
    public async Task Per_file_run_reports_each_files_own_state_and_continues_after_a_failure()
    {
        var q = new RunQueue(new[] { "a.jpg", "b.jpg", "c.jpg" }, perFile: true);
        var seen = new List<string>();
        var results = await q.RunAsync((files, ct) =>
        {
            Assert.Single(files);
            seen.Add(files[0]);
            return Task.FromResult(files[0] == "b.jpg" ? Fail("b is damaged") : Ok("out-" + files[0]));
        });

        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, seen);
        Assert.Equal(3, results.Count);
        Assert.Equal(new[] { QueueState.Done, QueueState.Failed, QueueState.Done }, q.Items.Select(i => i.State));
        Assert.Equal("b is damaged", q.Items[1].Message);
        Assert.Equal("out-a.jpg", q.Items[0].Message);
        Assert.Equal(2, q.DoneCount);
        Assert.Equal(1, q.FailedCount);
        Assert.True(q.Finished);
    }

    [Fact]
    public async Task Cancel_stops_before_the_next_file_and_cancels_the_token()
    {
        var q = new RunQueue(new[] { "a", "b", "c" }, perFile: true);
        CancellationToken seenToken = default;
        await q.RunAsync((files, ct) =>
        {
            seenToken = ct;
            if (files[0] == "a")
                q.Cancel(); // the Cancel button pressed while a runs
            return Task.FromResult(Ok("o"));
        });

        Assert.True(seenToken.IsCancellationRequested);
        Assert.Equal(new[] { QueueState.Done, QueueState.Cancelled, QueueState.Cancelled }, q.Items.Select(i => i.State));
        Assert.True(q.Finished);
    }

    [Fact]
    public async Task A_runner_that_honours_the_token_ends_as_cancelled_not_failed()
    {
        var q = new RunQueue(new[] { "a", "b" }, perFile: true);
        await q.RunAsync(async (files, ct) =>
        {
            q.Cancel();
            await Task.Delay(10, ct);
            return Ok("o");
        });
        Assert.All(q.Items, i => Assert.Equal(QueueState.Cancelled, i.State));
    }

    [Fact]
    public async Task Combined_run_marks_all_running_then_settles_from_one_result()
    {
        var q = new RunQueue(new[] { "a.pdf", "b.pdf" }, perFile: false);
        var during = new List<QueueState>();
        await q.RunAsync((files, ct) =>
        {
            Assert.Equal(new[] { "a.pdf", "b.pdf" }, files);
            during.AddRange(q.Items.Select(i => i.State));
            return Task.FromResult(Fail("cannot merge"));
        });
        Assert.All(during, s => Assert.Equal(QueueState.Running, s));
        Assert.All(q.Items, i =>
        {
            Assert.Equal(QueueState.Failed, i.State);
            Assert.Equal("cannot merge", i.Message);
        });
    }

    [Fact]
    public async Task Combined_success_marks_every_file_done()
    {
        var q = new RunQueue(new[] { "a.pdf", "b.pdf" }, perFile: false);
        await q.RunAsync((_, _) => Task.FromResult(Ok("merged.pdf")));
        Assert.All(q.Items, i => Assert.Equal(QueueState.Done, i.State));
    }

    [Fact]
    public async Task The_change_callback_fires_so_the_list_can_redraw()
    {
        var q = new RunQueue(new[] { "a", "b" }, perFile: true);
        var n = 0;
        await q.RunAsync((_, _) => Task.FromResult(Ok("o")), () => n++);
        Assert.True(n >= 4);
    }

    [Fact]
    public async Task Combine_is_ok_only_when_every_file_is_done_and_carries_the_runner_message()
    {
        var q = new RunQueue(new[] { "a", "b" }, perFile: true);
        var rs = await q.RunAsync((f, _) => Task.FromResult(f[0] == "b" ? Fail("bad pixel data") : Ok("o-a")));
        var c = q.Combine(rs, "t", "e");
        Assert.False(c.Ok);
        Assert.Equal("1 of 2 failed: b: bad pixel data", c.Error);
        Assert.Equal(new[] { "o-a" }, c.Outputs);

        var one = new RunQueue(new[] { "a" }, perFile: false);
        var r1 = await one.RunAsync((_, _) => Task.FromResult(Fail("exact words")));
        Assert.Equal("exact words", one.Combine(r1, "t", "e").Error);

        var good = new RunQueue(new[] { "a", "b" }, perFile: true);
        var rg = await good.RunAsync((f, _) => Task.FromResult(Ok("o-" + f[0])));
        var cg = good.Combine(rg, "t", "e");
        Assert.True(cg.Ok);
        Assert.Equal(new[] { "o-a", "o-b" }, cg.Outputs);

        var cancelled = new RunQueue(new[] { "a", "b" }, perFile: true);
        cancelled.Cancel();
        Assert.Equal("Cancelled", cancelled.Combine(Array.Empty<ToolResult>(), "t", "e").Error);
    }

    [Fact]
    public void Only_runners_verified_to_work_per_file_are_split()
    {
        var all = ToolRegistry.Default.All().Select(t => t.Entry).ToList();
        foreach (var id in new[] { "img-resize", "img-compress", "img-dpi" })
            Assert.True(RunQueue.IsPerFile(all.Single(e => e.Id == id)), id);
        // Combining tools must stay a single request.
        foreach (var id in new[] { "pdf-merge", "pdf-organize", "pdf-from-jpg" })
            Assert.False(RunQueue.IsPerFile(all.Single(e => e.Id == id)), id);
    }

    [Fact]
    public void Per_file_ids_are_real_image_runners()
    {
        var reg = ToolRegistry.Default.All();
        foreach (var id in new[] { "img-resize", "img-crop", "img-rotate", "img-compress", "img-to-jpg", "img-from-jpg", "img-convert-any", "img-watermark", "img-dpi" })
        {
            var t = reg.Single(x => x.Entry.Id == id);
            Assert.NotNull(t.Runner);
            Assert.True(t.Entry.MaxFiles > 1 || id is "img-convert-any" or "img-dpi", id);
        }
    }

    [Fact]
    public async Task Per_file_run_over_the_real_image_tool_produces_one_output_per_file()
    {
        var dir = ToolTestSupport.NewTempDir();
        var files = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var sub = IoPath.Combine(dir, "in" + i);
            IoDirectory.CreateDirectory(sub);
            var p = IoPath.Combine(sub, "same.png"); // same name in two folders
            using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40 + i * 10, 30))
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, p);
            files.Add(p);
        }
        var tool = ToolRegistry.Default.All().Single(t => t.Entry.Id == "img-compress");
        var outDir = IoPath.Combine(dir, "Netie Image");
        var q = RunQueue.For(tool.Entry, files);
        Assert.True(q.PerFile);

        var results = await q.RunAsync((fs, ct) =>
            ToolRunner.RunAsync(tool, new ToolRequest(fs, outDir, new Dictionary<string, string>()), null, ct));

        Assert.All(q.Items, i => Assert.Equal(QueueState.Done, i.State));
        var outputs = results.SelectMany(r => r.Outputs).ToList();
        Assert.Equal(2, outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(outputs, o => Assert.True(IoFile.Exists(o) && new System.IO.FileInfo(o).Length > 0));
        Assert.DoesNotContain(files, f => outputs.Contains(f));
    }
}
