using System.Globalization;
using NetieSpace.Apps.Shell;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>Wave 6 shell: friendly errors, dark palette, recents, window memory, keys and the command palette.</summary>
public class ShellFlowErrorTests
{
    [Theory]
    [InlineData("input file not found: C:\\x\\report.pdf", "pdf-merge", "report.pdf", ErrorAction.ChooseFile)]
    [InlineData("pdf-merge needs at least 2 input file(s), got 1", "pdf-merge", "2 files", ErrorAction.ChooseFile)]
    [InlineData("pdf-rotate accepts at most 1 input file(s), got 3", "pdf-rotate", "1 file", ErrorAction.ChooseFile)]
    [InlineData("input type not accepted by pdf-merge: C:\\a\\b.docx (accepts .pdf)", "pdf-merge", "b.docx", ErrorAction.ShowTools)]
    [InlineData("output directory is required", "pdf-merge", "folder", ErrorAction.ChangeFolder)]
    [InlineData("Cannot write to C:\\Windows\\x", "pdf-merge", "folder", ErrorAction.ChangeFolder)]
    [InlineData("option 'dpi': 'abc' is not an integer", "pdf-to-jpg", "dpi", ErrorAction.FixOptions)]
    [InlineData("InvalidOperationException: the file is password protected; run pdf-unlock first", "pdf-merge", "password", ErrorAction.OpenTool)]
    [InlineData("InvalidOperationException: no extractable text layer in a.pdf (scanned PDF? use pdf-ocr)", "pdf-to-word", "scan", ErrorAction.OpenTool)]
    [InlineData("InvalidOperationException: cannot read x.pdf as a PDF: bad xref", "pdf-rotate", "damaged", ErrorAction.OpenTool)]
    [InlineData("InvalidOperationException: cannot decode image a.png: bad data", "img-resize", "damaged", ErrorAction.ChooseFile)]
    [InlineData("IOException: The process cannot access the file 'a.pdf' because it is being used by another process.", "pdf-merge", "Another program", ErrorAction.TryAgain)]
    [InlineData("UnauthorizedAccessException: Access to the path 'C:\\x' is denied.", "pdf-merge", "Windows would not", ErrorAction.ChangeFolder)]
    [InlineData("claimed output is zero bytes: C:\\o\\a.pdf", "pdf-merge", "usable file", ErrorAction.TryAgain)]
    [InlineData("runner reported no output files", "pdf-merge", "usable file", ErrorAction.TryAgain)]
    [InlineData("InvalidOperationException: FFmpeg was not found (tools/ffmpeg or PATH)", "vid-trim", "FFmpeg", ErrorAction.ShowTools)]
    [InlineData("InvalidOperationException: the file already carries a digital signature; rewriting it would invalidate that signature", "pdf-rotate", "already signed", ErrorAction.ChooseFile)]
    [InlineData("cancelled", "pdf-merge", "stopped", ErrorAction.TryAgain)]
    public void Known_engine_errors_become_a_sentence_and_one_action(string raw, string toolId, string mustSay, ErrorAction action)
    {
        var f = FriendlyError.ForText(raw, toolId);
        Assert.Contains(mustSay, f.Sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(action, f.Action);
        // The technical text is kept for the Details fold, verbatim.
        Assert.Contains(raw, f.Details);
        // A person reads a sentence: no exception type names, no raw option syntax, no backslash paths.
        Assert.DoesNotContain("Exception", f.Sentence);
        Assert.DoesNotContain("\\", f.Sentence);
        Assert.EndsWith(".", f.Sentence);
        if (action != ErrorAction.None)
            Assert.False(string.IsNullOrWhiteSpace(f.ActionLabel));
    }

    [Fact]
    public void A_damaged_pdf_points_at_repair_but_the_repair_tool_itself_does_not_loop()
    {
        var other = FriendlyError.ForText("cannot read a.pdf as a PDF: bad", "pdf-merge");
        Assert.Equal("pdf-repair", other.ActionArg);
        var repair = FriendlyError.ForText("cannot read a.pdf as a PDF: bad", "pdf-repair");
        Assert.NotEqual(ErrorAction.OpenTool, repair.Action);
    }

    [Fact]
    public void A_locked_pdf_in_the_unlock_tool_offers_no_loop()
    {
        var f = FriendlyError.ForText("the file is password protected", "pdf-unlock");
        Assert.Equal(ErrorAction.None, f.Action);
    }

    [Fact]
    public void An_unknown_short_message_is_kept_as_a_sentence_and_a_long_or_technical_one_is_replaced()
    {
        var short1 = FriendlyError.ForText("the deck is already finished", "pdf-x");
        Assert.Equal("The deck is already finished.", short1.Sentence);
        Assert.Equal(ErrorAction.TryAgain, short1.Action);

        var tech = FriendlyError.ForText("NullReferenceException: Object reference not set\n   at Foo.Bar(String s) in C:\\src\\Foo.cs:line 12", "pdf-x");
        Assert.Equal("This tool could not finish. Try again, or pick a different file.", tech.Sentence);
        Assert.Contains("NullReferenceException", tech.Details);
    }

    [Fact]
    public void A_failed_batch_names_how_many_and_the_first_problem_in_plain_words()
    {
        var f = FriendlyError.ForText("2 of 3 failed: a.pdf: InvalidOperationException: cannot read a.pdf as a PDF: bad", "pdf-rotate");
        Assert.StartsWith("2 of 3 files could not be finished.", f.Sentence);
        Assert.Contains("a.pdf", f.Sentence);
        Assert.Contains("damaged", f.Sentence);
        Assert.Contains("2 of 3 failed", f.Details);
    }

    [Theory]
    [InlineData("engine missing: Word COM", "Microsoft Word")]
    [InlineData("engine missing: Excel COM", "Microsoft Excel")]
    [InlineData("engine missing: PowerPoint COM", "Microsoft PowerPoint")]
    [InlineData("engine missing: FFmpeg", "FFmpeg")]
    [InlineData("engine missing: Tesseract", "Tesseract")]
    [InlineData("engine missing: WebView2 runtime", "WebView2")]
    public void An_unavailable_tool_says_what_it_needs_by_the_name_a_person_knows(string availability, string needs)
    {
        var f = FriendlyError.ForUnavailable(availability, "pdf-from-word", null);
        Assert.Contains(needs, f.Sentence);
        Assert.StartsWith("This tool needs", f.Sentence);
        Assert.Equal(ErrorAction.ShowTools, f.Action);
        Assert.Contains(availability, f.Details);
    }

    [Theory]
    [InlineData("cloud only", "online service")]
    [InlineData("not built in this version", "not ready")]
    [InlineData("needs Space#3 disclose", "send your file")]
    [InlineData("licence not cleared for shipping: PyMuPDF", "not included")]
    public void Every_closed_availability_reason_has_a_human_sentence(string availability, string says)
    {
        var f = FriendlyError.ForUnavailable(availability, "x", null);
        Assert.Contains(says, f.Sentence);
        Assert.DoesNotContain("Space#3", f.Sentence);
        Assert.DoesNotContain("PyMuPDF", f.Sentence);
    }

    [Theory]
    [InlineData("engine missing: Word COM", "Needs Microsoft Word")]
    [InlineData("cloud only", "Needs an online service")]
    [InlineData("not built in this version", "Not ready yet")]
    [InlineData("needs Space#3 disclose", "Off until you agree")]
    [InlineData("licence not cleared for shipping: X", "Not included")]
    public void A_tile_says_why_in_a_few_words(string availability, string line) =>
        Assert.Equal(line, FriendlyError.Short(availability));

    [Fact]
    public void An_unavailable_tool_offers_a_working_alternative_of_the_same_kind_when_there_is_one()
    {
        HomeTile T(string id, string name, bool ok, string types) =>
            new(id, "X", name, "Convert", ok, ok ? null : "engine missing: Word COM", "Convert to PDF", types.Split(','));
        var word = T("pdf-from-word", "Word to PDF", false, ".doc,.docx");
        var other = T("pdf-from-docx2", "Docx to PDF", true, ".docx");
        var unrelated = T("pdf-from-jpg", "JPG to PDF", true, ".jpg");

        Assert.Same(other, FriendlyError.AlternativeFor(word, new[] { word, unrelated, other }));
        Assert.Null(FriendlyError.AlternativeFor(word, new[] { word, unrelated }));

        var f = FriendlyError.ForUnavailable("engine missing: Word COM", word.ToolId, other);
        Assert.Contains("Microsoft Word", f.Sentence);
        Assert.Contains("\"Docx to PDF\"", f.Sentence);
        Assert.Equal(ErrorAction.OpenTool, f.Action);
        Assert.Equal("pdf-from-docx2", f.ActionArg);
        Assert.Equal("Use Docx to PDF instead", f.ActionLabel);
    }

    [Fact]
    public void Every_error_text_a_tool_run_can_return_in_the_real_catalog_maps_without_throwing()
    {
        // Run the actual validator over the real catalog with bad input and feed every message through the mapper.
        var tools = ToolRegistry.Default.All();
        var seen = 0;
        foreach (var t in tools)
        {
            var bad = ToolRunner.Validate(t.Entry, new ToolRequest(new[] { "C:\\nope\\x.zzz" }, "o", new Dictionary<string, string>()), out _);
            if (bad is null) continue;
            var f = FriendlyError.ForText(bad, t.Entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(f.Sentence));
            Assert.DoesNotContain("Exception", f.Sentence);
            seen++;
        }
        Assert.True(seen > 20);
    }
}

public class ShellFlowThemeTests
{
    private static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Lin(int c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    private static double Contrast(string a, string b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    public static IEnumerable<object[]> Palettes() => new[] { new object[] { ShellPalette.Light }, new object[] { ShellPalette.Dark } };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Text_is_readable_on_every_surface_it_sits_on(ShellPalette p)
    {
        foreach (var bg in new[] { p.Paper, p.Sidebar, p.Surface })
        {
            Assert.True(Contrast(p.Ink, bg) >= 7, $"{p.Name}: ink on {bg} {Contrast(p.Ink, bg):0.0}");
            Assert.True(Contrast(p.Muted, bg) >= 4.5, $"{p.Name}: muted on {bg} {Contrast(p.Muted, bg):0.0}");
            Assert.True(Contrast(p.Danger, bg) >= 4.5, $"{p.Name}: danger on {bg} {Contrast(p.Danger, bg):0.0}");
            Assert.True(Contrast(p.Focus, bg) >= 4.5, $"{p.Name}: focus ring on {bg} {Contrast(p.Focus, bg):0.0}");
        }
        Assert.True(Contrast(p.Ink, p.Hover) >= 7, $"{p.Name}: ink on hover");
        Assert.True(Contrast(p.Ink, p.Press) >= 7, $"{p.Name}: ink on press");
        // A hairline only has to be seen, not read.
        Assert.True(Contrast(p.Line, p.Paper) >= 1.2, $"{p.Name}: line on paper");
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_token_is_a_hex_colour_and_has_a_key(ShellPalette p)
    {
        Assert.Equal(12, p.Tokens.Count);
        Assert.Equal(p.Tokens.Count, p.Tokens.Select(t => t.Key).Distinct().Count());
        foreach (var (key, hex) in p.Tokens)
        {
            Assert.StartsWith("Shell", key);
            Assert.Matches("^#[0-9A-F]{6}$", hex);
        }
    }

    [Fact]
    public void Dark_is_really_dark_and_light_is_really_light()
    {
        Assert.True(Luminance(ShellPalette.Dark.Paper) < 0.05);
        Assert.True(Luminance(ShellPalette.Light.Paper) > 0.9);
        Assert.True(Luminance(ShellPalette.Dark.Ink) > 0.7);
        // Both palettes define the same keys, so a switch can never leave one behind.
        Assert.Equal(ShellPalette.Light.Tokens.Select(t => t.Key), ShellPalette.Dark.Tokens.Select(t => t.Key));
    }

    [Fact]
    public void Each_app_keeps_its_accent_on_dark_and_its_icon_colour_is_readable_there()
    {
        var dark = ShellPalette.Dark;
        foreach (var light in AppPalette.All)
        {
            var d = light.ForDark();
            Assert.Equal(light.Accent, d.Accent);
            Assert.Equal(light.Ink, d.Ink);
            Assert.NotEqual(light.Tint, d.Tint);
            Assert.True(Luminance(d.Tint) < 0.05, $"{light.Key}: dark tint is dark");
            Assert.True(Contrast(d.Strong, d.Tint) >= 4.5, $"{light.Key}: icon on dark tint {Contrast(d.Strong, d.Tint):0.0}");
            foreach (var bg in new[] { dark.Paper, dark.Sidebar, dark.Surface })
                Assert.True(Contrast(d.Strong, bg) >= 4.5, $"{light.Key}: strong on {bg}");
            Assert.True(Contrast(d.Ink, d.Accent) >= 4.5, $"{light.Key}: button text");
            Assert.Matches("^#[0-9A-F]{6}$", d.Tint);
            Assert.Matches("^#[0-9A-F]{6}$", d.Strong);
        }
    }

    [Theory]
    [InlineData(0, "dark")]
    [InlineData(1, "light")]
    [InlineData(null, "light")]
    public void Windows_apps_use_light_theme_value_picks_the_palette(int? value, string expected) =>
        Assert.Equal(expected, ShellPalette.ForAppsUseLightTheme(value).Name);

    [Theory]
    [InlineData("dark", "dark")]
    [InlineData(" DARK ", "dark")]
    [InlineData("light", "light")]
    public void A_forced_theme_wins(string text, string expected) => Assert.Equal(expected, ShellPalette.Forced(text)!.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    public void No_forced_theme_follows_windows(string? text) => Assert.Null(ShellPalette.Forced(text));
}

public class ShellFlowRecentTests
{
    [Fact]
    public void Relative_time_reads_like_a_person_wrote_it()
    {
        var now = new DateTime(2026, 11, 21, 15, 0, 0);
        Assert.Equal("Just now", RelativeTime.Format(now.AddSeconds(-20), now));
        Assert.Equal("12 min ago", RelativeTime.Format(now.AddMinutes(-12), now));
        Assert.Equal("Today 09:05", RelativeTime.Format(new DateTime(2026, 11, 21, 9, 5, 0), now));
        Assert.Equal("Yesterday 23:30", RelativeTime.Format(new DateTime(2026, 11, 20, 23, 30, 0), now));
        Assert.Equal("3 days ago", RelativeTime.Format(now.AddDays(-3), now));
        Assert.Equal("2 Nov", RelativeTime.Format(new DateTime(2026, 11, 2, 8, 0, 0), now));
        Assert.Equal("2 Nov 2025", RelativeTime.Format(new DateTime(2025, 11, 2, 8, 0, 0), now));
        // A clock that moved back is shown as a date, never as "-5 min ago".
        Assert.Equal("21 Nov 15:10", RelativeTime.Format(now.AddMinutes(10), now));
    }

    [Fact]
    public void Recent_tools_come_back_tomorrow_newest_first_one_row_each_and_capped()
    {
        var dir = ToolTestSupport.NewTempDir();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rt = new RecentTools(IoPath.Combine(dir, "recent-tools.json"), cap: 3, clock: () => t = t.AddMinutes(1));
        rt.Add("pdf-merge");
        rt.Add("pdf-rotate");
        rt.Add("pdf-merge");
        rt.Add("pdf-split");
        rt.Add("pdf-compress");

        // A new process (tomorrow) reads the same file.
        var next = new RecentTools(rt.JsonPath).Load();
        Assert.Equal(new[] { "pdf-compress", "pdf-split", "pdf-merge" }, next.Select(r => r.ToolId));
    }

    [Fact]
    public void Recent_tools_drop_ids_the_catalog_no_longer_has_and_survive_a_damaged_file()
    {
        var dir = ToolTestSupport.NewTempDir();
        var path = IoPath.Combine(dir, "recent-tools.json");
        var rt = new RecentTools(path);
        rt.Add("pdf-merge");
        rt.Add("gone-tool");
        var known = new HashSet<string> { "pdf-merge" };
        Assert.Equal(new[] { "pdf-merge" }, rt.Load(known).Select(r => r.ToolId));

        IoFile.WriteAllText(path, "{ not json");
        Assert.Empty(rt.Load());
        Assert.Single(rt.Add("pdf-split")); // writes a clean file over the damaged one
        Assert.Empty(new RecentTools(IoPath.Combine(dir, "missing", "x.json")).Load());
    }

    [Fact]
    public void Recent_tools_ignore_a_blank_id()
    {
        var rt = new RecentTools(IoPath.Combine(ToolTestSupport.NewTempDir(), "r.json"));
        Assert.Empty(rt.Add("  "));
    }
}

public class ShellFlowWindowTests
{
    private static readonly WindowPlacement.Area Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void A_placement_that_fits_is_kept_exactly()
    {
        var p = new WindowPlacement(100, 80, 1100, 720, false);
        Assert.Equal(p, WindowPlacement.Fit(p, Screen, 720, 480));
    }

    [Fact]
    public void A_placement_from_an_unplugged_monitor_is_pulled_back_on_screen()
    {
        var p = new WindowPlacement(2500, 400, 1100, 720, false); // second monitor, now gone
        var fit = WindowPlacement.Fit(p, Screen, 720, 480)!;
        Assert.True(fit.Left <= 1920 - 120, $"left {fit.Left}");
        Assert.True(fit.Left + fit.Width >= 120);
        Assert.Equal(1100, fit.Width);

        var above = WindowPlacement.Fit(new WindowPlacement(100, -500, 1100, 720, false), Screen, 720, 480)!;
        Assert.True(above.Top >= 0, "the title bar must stay reachable");
        var below = WindowPlacement.Fit(new WindowPlacement(100, 5000, 1100, 720, false), Screen, 720, 480)!;
        Assert.True(below.Top <= 1080 - 40);
    }

    [Fact]
    public void A_window_bigger_than_a_smaller_screen_shrinks_but_never_below_its_minimum()
    {
        var p = new WindowPlacement(0, 0, 3000, 2000, false);
        var fit = WindowPlacement.Fit(p, new WindowPlacement.Area(0, 0, 1366, 768), 720, 480)!;
        Assert.Equal(1366, fit.Width);
        Assert.Equal(768, fit.Height);
        var tiny = WindowPlacement.Fit(new WindowPlacement(0, 0, 300, 200, false), Screen, 720, 480)!;
        Assert.Equal(720, tiny.Width);
        Assert.Equal(480, tiny.Height);
    }

    [Fact]
    public void Nonsense_placements_are_rejected_so_the_window_centres()
    {
        Assert.Null(WindowPlacement.Fit(null, Screen, 720, 480));
        Assert.Null(WindowPlacement.Fit(new WindowPlacement(double.NaN, 0, 800, 600, false), Screen, 720, 480));
        Assert.Null(WindowPlacement.Fit(new WindowPlacement(0, 0, 0, 0, false), Screen, 720, 480));
        Assert.Null(WindowPlacement.Fit(new WindowPlacement(0, 0, 800, 600, false), new WindowPlacement.Area(0, 0, 0, 0), 720, 480));
    }

    [Fact]
    public void The_store_round_trips_and_reads_a_missing_or_damaged_file_as_none()
    {
        var dir = ToolTestSupport.NewTempDir();
        var store = new WindowPlacementStore(IoPath.Combine(dir, "sub", "window.json"));
        Assert.Null(store.Load());
        var p = new WindowPlacement(10.5, 20, 1000, 700, true);
        store.Save(p);
        Assert.Equal(p, new WindowPlacementStore(store.JsonPath).Load());
        IoFile.WriteAllText(store.JsonPath, "garbage");
        Assert.Null(store.Load());
    }

    [Fact]
    public void Each_app_has_its_own_window_file()
    {
        Assert.NotEqual(WindowPlacementStore.DefaultPath(AppIdentity.Pdf), WindowPlacementStore.DefaultPath(AppIdentity.Image));
        Assert.EndsWith("window.json", WindowPlacementStore.DefaultPath(AppIdentity.Video));
    }
}

public class ShellFlowKeyTests
{
    [Theory]
    [InlineData("K", true, false, false, ShellCommand.Palette)]
    [InlineData("P", true, false, false, ShellCommand.Palette)]
    [InlineData("O", true, false, false, ShellCommand.OpenFile)]
    [InlineData("Left", false, true, false, ShellCommand.Back)]
    [InlineData("BrowserBack", false, false, false, ShellCommand.None)]
    [InlineData("F1", false, false, false, ShellCommand.Help)]
    [InlineData("K", false, false, false, ShellCommand.None)]
    [InlineData("K", true, false, true, ShellCommand.None)]
    [InlineData("K", true, true, false, ShellCommand.None)]
    [InlineData("Left", false, false, false, ShellCommand.None)]
    [InlineData("Left", true, false, false, ShellCommand.None)]
    [InlineData("F2", false, false, false, ShellCommand.None)]
    public void Chords_map_to_one_command(string key, bool ctrl, bool alt, bool shift, ShellCommand expected) =>
        Assert.Equal(expected, ShellKeys.Resolve(key, ctrl, alt, shift));

    [Fact]
    public void Help_lists_every_chord_the_map_answers_to()
    {
        var text = string.Join("\n", ShellKeys.HelpLines.Select(l => l.Chord));
        foreach (var chord in new[] { "Ctrl+K", "Ctrl+P", "Ctrl+O", "Alt+Left", "F1", "Tab" })
            Assert.Contains(chord, text);
    }
}

public class ShellFlowPaletteTests
{
    private static HomeTile T(string id, string name, string verb, string cat, string types, bool available = true) =>
        new(id, "X", name, verb, available, available ? null : "engine missing: Word COM", cat,
            types.Length == 0 ? Array.Empty<string>() : types.Split(','));

    private static readonly HomeTile[] Tiles =
    {
        T("pdf-merge", "Merge PDF", "Merge", "Organize PDF", ".pdf"),
        T("pdf-split", "Split PDF", "Split", "Organize PDF", ".pdf"),
        T("pdf-compress", "Compress PDF", "Compress", "Optimize PDF", ".pdf"),
        T("pdf-rotate", "Rotate PDF", "Rotate", "Edit PDF", ".pdf"),
        T("pdf-from-word", "Word to PDF", "Convert", "Convert to PDF", ".doc,.docx", available: false),
        T("pdf-from-jpg", "JPG to PDF", "Convert", "Convert to PDF", ".jpg,.png"),
    };

    private static IReadOnlyList<PaletteItem> All(string? pending = null, IEnumerable<RecentEntry>? files = null, IEnumerable<string>? tools = null) =>
        CommandPalette.Build(Tiles, files ?? Array.Empty<RecentEntry>(), tools ?? Array.Empty<string>(), pending);

    [Theory]
    [InlineData("rot", "pdf-rotate")]     // prefix of the name
    [InlineData("comp", "pdf-compress")]
    [InlineData("cmp", "pdf-compress")]   // letters in order
    [InlineData("mer", "pdf-merge")]
    [InlineData("jpg", "pdf-from-jpg")]
    [InlineData("word", "pdf-from-word")]
    [InlineData("docx", "pdf-from-word")] // accepted file type
    [InlineData("optim", "pdf-compress")] // category
    public void Two_or_three_letters_land_on_the_right_tool_first(string query, string expectedId)
    {
        var hits = CommandPalette.Search(All(), query);
        Assert.NotEmpty(hits);
        Assert.Equal(expectedId, hits[0].Key);
    }

    [Fact]
    public void A_name_that_starts_with_the_letters_beats_a_later_word_and_a_loose_match()
    {
        var items = new[]
        {
            new PaletteItem(PaletteKind.Tool, "a", "Split and merge", "x"),
            new PaletteItem(PaletteKind.Tool, "b", "Merge PDF", "x"),
            new PaletteItem(PaletteKind.Tool, "c", "Mirror image", "x"),
        };
        Assert.Equal(new[] { "b", "a" }, CommandPalette.Search(items, "mer").Select(i => i.Key));
        Assert.Equal("c", CommandPalette.Search(items, "mror")[0].Key); // in order, not contiguous
    }

    [Fact]
    public void Every_word_must_match_and_nothing_matching_is_an_empty_list()
    {
        Assert.Equal(new[] { "pdf-from-word", "pdf-from-jpg" }, CommandPalette.Search(All(), "convert pdf").Select(i => i.Key));
        Assert.Empty(CommandPalette.Search(All(), "zzzq"));
        Assert.Empty(CommandPalette.Search(All(), "word rotate"));
    }

    [Fact]
    public void Recent_files_are_searchable_by_name()
    {
        var files = new[] { new RecentEntry("/docs/Quarterly report.pdf", DateTime.UtcNow), new RecentEntry("/docs/Invoice.pdf", DateTime.UtcNow) };
        var hits = CommandPalette.Search(All(files: files), "quarter");
        Assert.Equal(PaletteKind.RecentFile, hits[0].Kind);
        Assert.Equal("/docs/Quarterly report.pdf", hits[0].Key);
        Assert.Equal("Quarterly report.pdf", hits[0].Title);
    }

    [Fact]
    public void An_empty_query_leads_with_tools_that_suit_the_open_file_then_recent_tools_then_recent_files()
    {
        var files = new[] { new RecentEntry("/d/a.pdf", DateTime.UtcNow) };
        var rows = CommandPalette.Search(All(pending: "x.jpg", files: files, tools: new[] { "pdf-rotate" }), null, max: 50);
        Assert.Equal("pdf-from-jpg", rows[0].Key);     // suits x.jpg
        Assert.Equal("pdf-rotate", rows[1].Key);        // used lately
        Assert.Equal(PaletteKind.RecentFile, rows[2].Kind);
        Assert.Equal(rows.Count, rows.Select(r => r.Kind + r.Key).Distinct().Count()); // nothing twice
        Assert.Contains(rows, r => r.Key == PaletteItem.CommandOpenFile);
    }

    [Fact]
    public void An_unavailable_tool_is_listed_dimmed_with_its_reason_and_the_list_is_capped()
    {
        var word = All().Single(i => i.Key == "pdf-from-word");
        Assert.False(word.Enabled);
        Assert.Contains("not available", word.Subtitle);
        Assert.Equal(CommandPalette.MaxRows, CommandPalette.Search(Enumerable.Range(0, 30).Select(i => new PaletteItem(PaletteKind.Tool, "t" + i, "Tool " + i, "x")).ToList(), null).Count);
        Assert.Equal(3, CommandPalette.Search(All(), "pdf", max: 3).Count);
    }

    [Theory]
    [InlineData(0, 1, 5, 1)]
    [InlineData(4, 1, 5, 0)]   // wraps down
    [InlineData(0, -1, 5, 4)]  // wraps up
    [InlineData(-1, 1, 5, 0)]  // nothing selected yet
    [InlineData(2, 1, 0, -1)]  // an empty list selects nothing
    public void Arrow_keys_move_and_wrap(int current, int delta, int count, int expected) =>
        Assert.Equal(expected, CommandPalette.Move(current, delta, count));
}

public class ShellFlowDropTests
{
    private static HomeTile T(string id, string name, string cat, string types, bool ok = true) =>
        new(id, "X", name, "Run", ok, ok ? null : "no", cat, types.Split(','));

    [Fact]
    public void A_dropped_file_shows_at_most_six_suggestions_named_for_the_file_and_the_whole_grid_below()
    {
        var tiles = Enumerable.Range(1, 9).Select(i => T($"t{i}", $"Tool {i}", "Cat", ".pdf")).ToList();
        var cats = new[] { new HomeCategory("Cat", tiles) };
        var grid = HomeGrid.Build(cats, null, null, "/in/Report.pdf");
        Assert.Equal("Suggested for Report.pdf", grid.OpenWithTitle);
        Assert.Equal(6, grid.OpenWith.Count);
        Assert.Equal(9, grid.Categories.Sum(c => c.Tiles.Count));
    }

    [Fact]
    public void A_file_no_tool_takes_gets_no_suggestion_heading()
    {
        var cats = new[] { new HomeCategory("Cat", new[] { T("a", "A", "Cat", ".pdf") }) };
        var grid = HomeGrid.Build(cats, null, null, "/in/notes.xyz");
        Assert.Null(grid.OpenWithTitle);
        Assert.Empty(grid.OpenWith);
    }
}

public class ShellFlowSnapshotArgsTests
{
    [Fact]
    public void The_shell_flow_flags_are_read_next_to_snapshot()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", "o.png", "--theme", "DARK", "--with-file", "/in/a.pdf",
            "--overlay", "palette", "--query", "rot", "--seed-recents", "/a.pdf;/b.pdf", "--audit", "a.txt" });
        Assert.Null(a.Error);
        Assert.Equal("dark", a.Theme);
        Assert.Equal("/in/a.pdf", a.WithFile);
        Assert.Equal("palette", a.Overlay);
        Assert.Equal("rot", a.Query);
        Assert.Equal("/a.pdf;/b.pdf", a.SeedRecents);
        Assert.Equal("a.txt", a.AuditPath);
    }

    [Fact]
    public void Without_the_extras_everything_is_null_so_the_old_snapshots_are_unchanged()
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", "o.png" });
        Assert.Null(a.Theme);
        Assert.Null(a.WithFile);
        Assert.Null(a.Overlay);
        Assert.Null(a.Query);
        Assert.Null(a.SeedRecents);
        Assert.Null(a.AuditPath);
    }

    [Theory]
    [InlineData("--theme", "purple", "--theme")]
    [InlineData("--overlay", "toast", "--overlay")]
    [InlineData("--overlay", "--query", "--overlay")]
    public void A_bad_value_is_an_error_that_names_the_flag(string flag, string value, string named)
    {
        var a = SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", "o.png", flag, value });
        Assert.NotNull(a.Error);
        Assert.Contains(named, a.Error);
    }
}
