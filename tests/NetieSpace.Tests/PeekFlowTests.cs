using System.Xml.Linq;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Tests;

/// <summary>
/// Wave 6 Quick Look flow (founder: "Scale all ... improve ui ux and real use case"): a student peeks a lecture
/// PDF, scrolls, finds "exam", steps to the next file, copies, presses Esc. Rules live in PeekFlowRules; the
/// window is judged from the desktop job screenshots.
/// </summary>
public class PeekFlowTests
{
    private static string Repo(params string[] parts) =>
        Path.Combine(new[] { ProjectRoot.Find() }.Concat(parts).ToArray());

    private static string TempFolder()
    {
        var d = Path.Combine(Path.GetTempPath(), "peekflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- 1. keys: which arrows step files ----

    [Theory]
    [InlineData(PreviewKind.Pdf, true)]
    [InlineData(PreviewKind.Text, true)]
    [InlineData(PreviewKind.Markdown, true)]
    [InlineData(PreviewKind.Html, true)]
    [InlineData(PreviewKind.Office, true)]
    [InlineData(PreviewKind.Image, false)]
    [InlineData(PreviewKind.Video, false)]
    [InlineData(PreviewKind.Binary, false)]
    [InlineData(PreviewKind.ShellInfo, false)]
    public void Content_that_scrolls_keeps_Up_Down_Home_End_and_supports_find(PreviewKind kind, bool scrolls)
    {
        Assert.Equal(scrolls, PeekFlowRules.Scrolls(kind));
        Assert.Equal(!scrolls, PeekFlowRules.ArrowStepsFile(kind, vertical: true));
        Assert.Equal(!scrolls, PeekFlowRules.HomeEndStepsFile(kind));
        Assert.Equal(scrolls, PeekFlowRules.FindSupported(kind));
    }

    [Theory]
    [InlineData(PreviewKind.Pdf)]
    [InlineData(PreviewKind.Image)]
    [InlineData(PreviewKind.Video)]
    [InlineData(PreviewKind.Office)]
    public void Left_and_Right_step_files_for_every_kind(PreviewKind kind) =>
        Assert.True(PeekFlowRules.ArrowStepsFile(kind, vertical: false));

    // ---- 2. folder order, wrap, folders skipped, "3 of 12" ----

    [Fact]
    public void Natural_order_puts_file2_before_file10_and_ignores_case()
    {
        var names = new[] { "file10.pdf", "File2.pdf", "file1.pdf", "lecture_01.pdf", "Lecture_02.png" };
        var sorted = names.OrderBy(n => n, Comparer<string>.Create(PeekFlowRules.NaturalCompare)).ToArray();
        Assert.Equal(new[] { "file1.pdf", "File2.pdf", "file10.pdf", "lecture_01.pdf", "Lecture_02.png" }, sorted);
        Assert.True(PeekFlowRules.NaturalCompare("a01", "a1") != 0, "equal numbers still order deterministically");
        Assert.Equal(0, PeekFlowRules.NaturalCompare("same.txt", "same.txt"));
    }

    [Fact]
    public void Siblings_skip_folders_hidden_files_and_office_locks_and_sort_naturally()
    {
        var dir = TempFolder();
        try
        {
            foreach (var n in new[] { "b10.txt", "b2.txt", "a.pdf", "~$a.docx", "desktop.ini", "Thumbs.db" })
                File.WriteAllText(Path.Combine(dir, n), "x");
            Directory.CreateDirectory(Path.Combine(dir, "subfolder"));
            File.WriteAllText(Path.Combine(dir, "subfolder", "inner.txt"), "x");
            // Windows hides by attribute, Unix by a leading dot: either way the file must not be a "next file".
            var hidden = Path.Combine(dir, OperatingSystem.IsWindows() ? "secret.txt" : ".secret.txt");
            File.WriteAllText(hidden, "x");
            if (OperatingSystem.IsWindows())
                File.SetAttributes(hidden, FileAttributes.Hidden);

            var list = PeekFlowRules.SiblingFiles(Path.Combine(dir, "a.pdf"));
            Assert.Equal(new[] { "a.pdf", "b2.txt", "b10.txt" }, list.Select(Path.GetFileName).ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Step_wraps_at_both_ends_and_returns_null_for_a_lone_file()
    {
        var f = new[] { "d/a", "d/b", "d/c" };
        Assert.Equal("d/b", PeekFlowRules.Step(f, "d/a", 1));
        Assert.Equal("d/a", PeekFlowRules.Step(f, "d/c", 1));
        Assert.Equal("d/c", PeekFlowRules.Step(f, "d/a", -1));
        Assert.Equal("d/A", PeekFlowRules.Step(new[] { "d/A", "d/B" }, "D/B", 1));
        Assert.Null(PeekFlowRules.Step(new[] { "d/a" }, "d/a", 1));
        Assert.Null(PeekFlowRules.Step(Array.Empty<string>(), "d/a", 1));
        Assert.Equal("d/a", PeekFlowRules.Step(f, "d/gone", 1));
        Assert.Equal("d/c", PeekFlowRules.Step(f, "d/gone", -1));
        Assert.Equal("d/a", PeekFlowRules.Extreme(f, toEnd: false));
        Assert.Equal("d/c", PeekFlowRules.Extreme(f, toEnd: true));
    }

    [Fact]
    public void Counter_reads_3_of_12_and_hides_for_a_lone_file()
    {
        var f = Enumerable.Range(1, 12).Select(i => $"d/f{i:00}").ToArray();
        Assert.Equal("3 of 12", PeekFlowRules.CounterText(f, "d/f03"));
        Assert.Equal("12 of 12", PeekFlowRules.CounterText(f, "D/F12"));
        Assert.Equal("", PeekFlowRules.CounterText(new[] { "d/x" }, "d/x"));
        Assert.Equal("", PeekFlowRules.CounterText(f, "d/other"));
    }

    // ---- 3. readouts ----

    [Fact]
    public void Page_and_find_readouts()
    {
        Assert.Equal("Page 7 / 40", PeekFlowRules.PageText(7, 40));
        Assert.Equal("Page 40 / 40", PeekFlowRules.PageText(99, 40));
        Assert.Equal("Page 1 / 40", PeekFlowRules.PageText(0, 40));
        Assert.Equal("", PeekFlowRules.PageText(1, 0));

        Assert.Equal("", PeekFlowRules.FindText("", 0, -1));
        Assert.Equal("No matches", PeekFlowRules.FindText("exam", 0, -1));
        Assert.Equal("1 of 9", PeekFlowRules.FindText("exam", 9, 0));
        Assert.Equal("9 of 9", PeekFlowRules.FindText("exam", 9, 8));

        Assert.Equal(1, PeekFlowRules.NextMatch(0, 3, 1));
        Assert.Equal(0, PeekFlowRules.NextMatch(2, 3, 1));
        Assert.Equal(2, PeekFlowRules.NextMatch(0, 3, -1));
        Assert.Equal(-1, PeekFlowRules.NextMatch(0, 0, 1));
    }

    // ---- 4. timing contract (the desktop job parses these lines) ----

    [Fact]
    public void Timing_line_round_trips_and_the_budget_is_600_ms()
    {
        var line = PeekFlowRules.TimingLine(PreviewKind.Pdf, 412, warm: true);
        Assert.Equal("[peek] open-to-content kind=Pdf ms=412 warm=1", line);
        Assert.True(PeekFlowRules.TryParseTimingLine("2026-11-21 12:00:00 INFO " + line, out var kind, out var ms, out var warm));
        Assert.Equal("Pdf", kind);
        Assert.Equal(412, ms);
        Assert.True(warm);
        Assert.False(PeekFlowRules.TryParseTimingLine("[peek] something else", out _, out _, out _));
        Assert.Equal(600, PeekFlowRules.OpenBudgetMs);
        Assert.True(PeekFlowRules.WithinBudget(600));
        Assert.False(PeekFlowRules.WithinBudget(601));
        Assert.False(PeekFlowRules.WithinBudget(-1));
    }

    // ---- 5. cheat sheet ----

    [Fact]
    public void Cheat_sheet_names_every_flow_key()
    {
        var keys = string.Join(" | ", PeekFlowRules.CheatSheet.Select(r => r.Keys));
        foreach (var k in new[] { "Left / Right", "Up / Down", "Page Up", "Home / End", "Ctrl+F", "Ctrl+C", "Ctrl +", "?", "Esc" })
            Assert.Contains(k, keys);
        Assert.True(PeekFlowRules.CheatSheet.Count <= 10, "a small cheat sheet, not a manual");
    }

    // ---- 6. injected page scripts ----

    [Fact]
    public void Find_script_exposes_run_step_clear_and_skips_script_text()
    {
        var js = PeekScripts.Find;
        foreach (var api in new[] { "window.__netieFind", "run: function", "step: function", "clear: function", "mark.netie-find", "netie-cur" })
            Assert.Contains(api, js);
        Assert.Contains("'SCRIPT'", js);
        Assert.Contains("'STYLE'", js);
        Assert.DoesNotContain("—", js);
    }

    [Fact]
    public void Key_bridge_reports_find_zoom_help_and_never_steals_typing()
    {
        var js = PeekScripts.KeyBridge;
        foreach (var token in new[] { "'find'", "'zoomIn'", "'zoomOut'", "'zoomReset'", "'help'", "'findNext'", "'findPrev'", "type: 'peekKey'" })
            Assert.Contains(token, js);
        Assert.Contains("typing(e.target)", js);
    }
    // ---- 7. window wiring (pinned from the sources; the rendered result is judged from the desktop screenshots) ----

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument PreviewXaml() => XDocument.Load(Repo("src", "NetieSpace", "Views", "PreviewWindow.xaml"));
    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(e => e.Attribute(X + "Name")?.Value == name);
    private static bool Inside(XElement e, string ancestorName) =>
        e.Ancestors().Any(a => a.Attribute(X + "Name")?.Value == ancestorName);
    private static string Cs() => File.ReadAllText(Repo("src", "NetieSpace", "Views", "PreviewWindow.xaml.cs"));

    [Fact]
    public void Header_carries_the_counter_and_the_file_name_for_automation()
    {
        var doc = PreviewXaml();
        var counter = Named(doc, "PeekCounterText");
        Assert.Equal("PeekCounter", counter.Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.True(Inside(counter, "PeekCounterChip"));
        Assert.False(Inside(counter, "PeekHeaderRow"), "the counter sits beside the name, not in the button row");
        Assert.Equal("PeekFileName", Named(doc, "FileNameText").Attribute("AutomationProperties.AutomationId")?.Value);
    }

    [Fact]
    public void Find_bar_and_page_readout_live_in_the_content_toolbar_not_over_the_WebView()
    {
        var doc = PreviewXaml();
        foreach (var name in new[] { "PeekFindBar", "PeekFindBox", "PeekFindCount", "PeekFindPrevButton", "PeekFindNextButton", "PeekFindCloseButton", "PeekPageText" })
            Assert.True(Inside(Named(doc, name), "PeekToolbar"), name);
        Assert.Equal("PeekFindBox", Named(doc, "PeekFindBox").Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal("PeekFindCount", Named(doc, "PeekFindCount").Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal("PeekPage", Named(doc, "PeekPageText").Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal("Collapsed", Named(doc, "PeekFindBar").Attribute("Visibility")?.Value);
    }

    [Fact]
    public void Help_sheet_is_a_popup_so_it_draws_over_the_WebView_and_the_skeleton_is_in_the_content_well()
    {
        var doc = PreviewXaml();
        var help = Named(doc, "PeekHelpPopup");
        Assert.Equal("Popup", help.Name.LocalName);
        Assert.Equal("False", help.Attribute("StaysOpen")?.Value);
        Assert.Contains(help.Descendants(), e => e.Attribute(X + "Name")?.Value == "PeekHelpTitle");
        Assert.True(Inside(Named(doc, "PeekSkeleton"), "ContentHost"));
        Assert.Equal("Collapsed", Named(doc, "PeekSkeleton").Attribute("Visibility")?.Value);
    }

    [Fact]
    public void Window_steps_files_through_the_rules_not_through_Explorer_arrow_posting()
    {
        var cs = Cs();
        Assert.Contains("PeekFlowRules.Step(files, _currentFilePath, delta)", cs);
        Assert.Contains("PeekFlowRules.Extreme(files, toEnd)", cs);
        Assert.Contains("PeekFlowRules.CounterText(files, path)", cs);
        Assert.DoesNotContain("TryPostArrowToExplorer", cs);
        // The Explorer highlight follows, off the UI thread.
        Assert.Contains("SyncExplorerSelection(next)", cs);
        Assert.Contains("Task.Run(() =>", cs);
    }

    [Fact]
    public void Hook_leaves_Up_Down_Home_End_to_scrolling_documents_only_when_the_peek_is_in_front()
    {
        var app = File.ReadAllText(Repo("src", "NetieSpace", "App.xaml.cs"));
        Assert.Contains("Win32Api.IsOurProcessForeground() && !window.StepsFilesForKey(vk)", app);
        Assert.Contains("Win32Api.IsOurProcessForeground() && !window.HomeEndStepsFiles()", app);
    }

    [Fact]
    public void Esc_peels_help_then_find_then_the_peek_and_Ctrl_F_Ctrl_C_zoom_keys_are_wired()
    {
        var cs = Cs();
        var pop = cs[cs.IndexOf("public bool TryPopPreviewStack()", StringComparison.Ordinal)..];
        Assert.True(pop.IndexOf("PeekHelpPopup.IsOpen", StringComparison.Ordinal) < pop.IndexOf("_findOpen", StringComparison.Ordinal));
        Assert.True(pop.IndexOf("_findOpen", StringComparison.Ordinal) < pop.IndexOf("_previewStack.Count == 0", StringComparison.Ordinal));
        Assert.Contains("ctrl && e.Key == Key.F)", cs);
        Assert.Contains("case \"zoomReset\": FitContent(); break;", cs);
        Assert.Contains("PeekScripts.KeyBridge", cs);
        Assert.Contains("type == \"findResult\"", cs);
        // Browser accelerators stay off (Esc and Ctrl+W belong to the host); the bridge forwards what the peek wants.
        Assert.Contains("AreBrowserAcceleratorKeysEnabled = false", cs);
    }

    [Fact]
    public void Open_to_content_is_logged_once_per_open_in_the_format_the_desktop_check_parses()
    {
        var cs = Cs();
        Assert.Contains("PeekFlowRules.TimingLine(kind, ms, _openWarm)", cs);
        Assert.Contains("_openClock.Restart();", cs);
        Assert.Contains("ShowSkeleton();", cs);
        Assert.Contains("MarkContentReady(PreviewKind.Pdf)", cs);
        Assert.Contains("MarkContentReady(PreviewKind.Video)", cs);
    }

    [Fact]
    public void PDF_viewer_reports_find_results_scrolls_by_key_and_has_a_selectable_text_layer()
    {
        var html = File.ReadAllText(Repo("src", "NetieSpace", "Assets", "pdfjs", "netie-viewer.html"));
        Assert.Contains("type: 'findResult'", html);
        Assert.Contains("function scrollKey(key)", html);
        Assert.Contains("msg?.type === 'scrollKey'", html);
        Assert.Contains("new pdfjsLib.TextLayer(", html);
        Assert.Contains(".textLayer", html);
        Assert.Contains("searchRun", html);
    }

    [Fact]
    public void Desktop_check_drives_the_whole_flow_and_asserts_each_step()
    {
        var ps = File.ReadAllText(Repo("scripts", "check-desktop.ps1"));
        foreach (var needle in new[]
        {
            "function New-LecturePdf", "New-LecturePdf (Join-Path $lec \"lecture_01_notes.pdf\") 40",
            "flow: Right shows", "flow: find reports a match count > 0", "flow: Page Down moves the page indicator",
            "flow: Esc closes the peek", "flow: Ctrl+C copies the selected PDF text", "flow: ? shows the keyboard cheat sheet",
            "flow: Right on the last file wraps to the first", "open-to-content", "-le 600",
            "flow-1-pdf-open.png", "flow-4-find.png", "flow-9-help.png",
        })
            Assert.Contains(needle, ps);
        Assert.DoesNotContain("\u2014", ps);
    }
}
