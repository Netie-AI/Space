using NetieSpace.Apps.Pdf;
using PdfSharpCore.Drawing;
using Sharp = PdfSharpCore.Pdf;
using Pig = UglyToad.PdfPig;
using Shell = NetieSpace.Apps.Shell;

namespace NetieSpace.Tests;

/// <summary>
/// The office-worker flow behind the PDF panels: three documents, reorder, delete blanks, rotate the scan, merge, sign.
/// The panels are WPF and only Windows CI draws them; everything they decide is data and is proven here by reading the
/// finished files back with PdfPig.
/// </summary>
public class PdfFlowTests
{
    /// <summary>A PDF whose page i says "{tag}-{i:00}"; pages in <paramref name="blank"/> carry no text at all.</summary>
    private static string Doc(string dir, string name, int pages, string tag, params int[] blank)
    {
        using var doc = new Sharp.PdfDocument();
        for (var i = 1; i <= pages; i++)
        {
            var page = doc.AddPage();
            page.Width = 612; page.Height = 792;
            if (blank.Contains(i)) continue;
            using var g = XGraphics.FromPdfPage(page);
            g.DrawString($"{tag}-{i:00}", new XFont("Arial", 20), XBrushes.Black, new XPoint(60, 100));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    private static string[] Lines(string path) => PdfKit.Texts(path).Select(t => t.Trim()).ToArray();

    // ---- plan text ----

    [Fact]
    public void Plan_text_folds_runs_and_keeps_rotation_and_blanks()
    {
        var pages = new[]
        {
            new PlanPage(1, 1, 0), new PlanPage(1, 2, 0), new PlanPage(1, 3, 0),
            new PlanPage(2, 5, 90), new PlanPage(2, 6, 90), new PlanPage(2, 7, 0),
            PlanPage.Blank, new PlanPage(3, 1, 270),
        };
        Assert.Equal("1:1-3;2:5-6@90;2:7;blank;3:1@270", PdfPagePlan.ToPlanString(pages));
        Assert.Equal("", PdfPagePlan.ToPlanString(Array.Empty<PlanPage>()));
    }

    [Fact]
    public void Plan_text_does_not_fold_pages_that_were_reordered()
    {
        var pages = new[] { new PlanPage(1, 3, 0), new PlanPage(1, 2, 0), new PlanPage(1, 1, 0) };
        Assert.Equal("1:3;1:2;1:1", PdfPagePlan.ToPlanString(pages));
    }

    [Fact]
    public void Remove_spec_lists_what_is_missing_as_ranges()
    {
        Assert.Equal("2,5-7", PdfPagePlan.RemoveSpec(8, new[] { 1, 3, 4, 8 }));
        Assert.Equal("", PdfPagePlan.RemoveSpec(3, new[] { 1, 2, 3 }));
        Assert.Equal("1-3", PdfPagePlan.RemoveSpec(3, Array.Empty<int>()));
    }

    [Fact]
    public void Move_block_keeps_the_order_inside_the_block()
    {
        var l = new List<char> { 'a', 'b', 'c', 'd', 'e' };
        var start = PdfPagePlan.MoveBlock(l, new[] { 3, 1 }, 5);
        Assert.Equal("acebd", new string(l.ToArray()));
        Assert.Equal(3, start);

        l = new List<char> { 'a', 'b', 'c', 'd', 'e' };
        start = PdfPagePlan.MoveBlock(l, new[] { 4 }, 0);
        Assert.Equal("eabcd", new string(l.ToArray()));
        Assert.Equal(0, start);
    }

    // ---- the grid as data ----

    [Fact]
    public void Click_ctrl_and_shift_select_like_explorer()
    {
        var g = new PageGridModel(PageGridRules.Organize);
        g.Load(new[] { 10 });
        g.Click(2, false, false);
        Assert.Equal(new[] { 2 }, g.Selected.OrderBy(x => x));
        g.Click(5, true, false);
        Assert.Equal(new[] { 2, 3, 4, 5 }, g.Selected.OrderBy(x => x));
        g.Click(8, false, true);
        Assert.Equal(new[] { 2, 3, 4, 5, 8 }, g.Selected.OrderBy(x => x));
        g.Click(3, false, true);
        Assert.DoesNotContain(3, g.Selected);
        g.Click(0, false, false);
        Assert.Equal(new[] { 0 }, g.Selected);
    }

    [Fact]
    public void Rotate_delete_blank_move_and_undo()
    {
        var g = new PageGridModel(PageGridRules.Organize);
        g.Load(new[] { 4, 2 });
        Assert.False(g.IsChanged);

        g.Click(1, false, false);
        Assert.True(g.RotateSelected(90));
        Assert.True(g.RotateSelected(90));
        Assert.Equal(180, g.Pages[1].Rotation);
        Assert.True(g.RotateSelected(-90));
        Assert.Equal(90, g.Pages[1].Rotation);

        g.Click(4, false, false);
        Assert.Equal(1, g.DeleteSelected());
        Assert.Equal(5, g.Pages.Count);
        Assert.Equal(new PlanPage(2, 2, 0), g.Pages[4]);

        g.Click(0, false, false);
        Assert.True(g.InsertBlank());
        Assert.True(g.Pages[1].IsBlankInsert);

        g.ClearSelection();
        g.Click(0, false, false);
        Assert.True(g.MoveSelected(g.Pages.Count));
        Assert.Equal(new PlanPage(1, 1, 0), g.Pages[^1]);
        Assert.Equal(new[] { g.Pages.Count - 1 }, g.Selected);
        Assert.True(g.IsChanged);

        while (g.CanUndo) g.Undo();
        Assert.False(g.IsChanged);
        Assert.Equal(6, g.Pages.Count);
    }

    [Fact]
    public void Rules_gate_each_action()
    {
        var g = new PageGridModel(PageGridRules.Remove);
        g.Load(new[] { 3 });
        g.SelectAll();
        Assert.False(g.RotateSelected(90));
        Assert.False(g.MoveSelected(0));
        Assert.False(g.InsertBlank());
        g.ClearSelection();
        g.Click(1, false, false);
        Assert.Equal(1, g.DeleteSelected());
        Assert.Equal("2", g.RemovedSpec());

        var r = new PageGridModel(PageGridRules.Rotate);
        r.Load(new[] { 3 });
        r.Click(0, false, false);
        Assert.Equal(0, r.DeleteSelected());
        Assert.True(r.RotateSelected(90));
    }

    [Fact]
    public void Reset_restores_the_loaded_pages_and_can_be_undone()
    {
        var g = new PageGridModel(PageGridRules.Organize);
        g.Load(new[] { 3 });
        g.Click(0, false, false);
        g.DeleteSelected();
        g.Reset();
        Assert.False(g.IsChanged);
        Assert.Equal(3, g.Pages.Count);
        Assert.True(g.Undo());
        Assert.Equal(2, g.Pages.Count);
    }

    // ---- blank pages ----

    private static byte[] Paper(int w, int h, byte grey)
    {
        var b = new byte[w * h * 4];
        for (var i = 0; i < b.Length; i += 4) { b[i] = b[i + 1] = b[i + 2] = grey; b[i + 3] = 255; }
        return b;
    }

    private static void Ink(byte[] b, int w, int x0, int x1, int y0, int y1, byte v)
    {
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * w + x) * 4;
                b[i] = b[i + 1] = b[i + 2] = v;
            }
    }

    [Fact]
    public void Blank_page_detection_reads_pixels()
    {
        const int w = 160, h = 208;
        Assert.True(PdfBlankPage.IsBlank(Paper(w, h, 255), w, h, w * 4));

        var grey = Paper(w, h, 222);                          // a scanner's paper tone, one speck of dust
        Ink(grey, w, 80, 82, 100, 102, 120);
        Assert.True(PdfBlankPage.IsBlank(grey, w, h, w * 4));

        var text = Paper(w, h, 255);                         // two lines of text
        Ink(text, w, 20, 120, 30, 33, 20);
        Ink(text, w, 20, 100, 40, 43, 20);
        Assert.False(PdfBlankPage.IsBlank(text, w, h, w * 4));

        var word = Paper(w, h, 255);                         // one short word of small print is still not blank
        Ink(word, w, 40, 60, 60, 63, 30);
        Assert.False(PdfBlankPage.IsBlank(word, w, h, w * 4));

        Assert.False(PdfBlankPage.IsBlank(Paper(w, h, 10), w, h, w * 4));   // a dark picture is not blank
        Assert.False(PdfBlankPage.IsBlank(new byte[4], 1, 1, 4));            // unknown is not blank
    }

    // ---- snapshot states ----

    [Fact]
    public void Snapshot_args_carry_files_and_state_for_a_panel()
    {
        var a = Shell.SnapshotArgs.Parse(new[] { "--app", "pdf", "--snapshot", @"C:\o\m.png", "--view", "pdf-merge", "--files", @"C:\a b\x.pdf|C:\a b\y.pdf", "--state", "select=2;rotate=90" });
        Assert.Null(a.Error);
        Assert.Equal(new[] { @"C:\a b\x.pdf", @"C:\a b\y.pdf" }, a.Files);
        Assert.Equal("select=2;rotate=90", a.State);
        Assert.Null(Shell.SnapshotArgs.Parse(new[] { "--snapshot", "o.png", "--view", "pdf-merge" }).Files);
    }

    [Fact]
    public void Snapshot_files_need_a_view_and_a_value()
    {
        Assert.NotNull(Shell.SnapshotArgs.Parse(new[] { "--snapshot", "o.png", "--files", "a.pdf" }).Error);
        Assert.NotNull(Shell.SnapshotArgs.Parse(new[] { "--snapshot", "o.png", "--view", "pdf-merge", "--files" }).Error);
        Assert.NotNull(Shell.SnapshotArgs.Parse(new[] { "--snapshot", "o.png", "--view", "pdf-merge", "--state" }).Error);
    }

    // ---- drops and placement ----

    [Fact]
    public void Drops_keep_existing_pdfs_and_name_the_rest()
    {
        var r = PdfDropRules.Filter(new[] { "C:\\a\\x.PDF", "C:\\a\\notes.txt", "C:\\a\\gone.pdf", "C:\\a\\scan.jpg" }, p => !p.Contains("gone"));
        Assert.Equal(new[] { "C:\\a\\x.PDF" }, r.Accepted);
        Assert.Equal(3, r.Skipped.Count);
        Assert.Contains("3 files skipped", r.Message);
        Assert.Null(PdfDropRules.Filter(new[] { "C:\\a\\x.pdf" }, _ => true).Message);
    }

    [Fact]
    public void Click_becomes_percent_text_that_parses_back()
    {
        Assert.Equal("62.5,88", PdfSignPlacement.FromClick(250, 704, 400, 800));
        Assert.True(PdfSignPlacement.TryParse("62.5,88", out var x, out var y));
        Assert.Equal((62.5, 88.0), (x, y));
        Assert.Equal("0,100", PdfSignPlacement.Format(-5, 140));
        Assert.False(PdfSignPlacement.TryParse("101,5", out _, out _));
        Assert.False(PdfSignPlacement.TryParse("a,b", out _, out _));
        Assert.False(PdfSignPlacement.TryParse("5", out _, out _));
        Assert.False(PdfSignPlacement.TryParse("NaN,5", out _, out _));
    }

    [Fact]
    public void Block_origin_stays_inside_the_page_edge()
    {
        var (l, t) = PdfSignPlacement.BlockOrigin(100, 100, 612, 792, 150, 50, 24);
        Assert.Equal(612 - 24 - 150, l);
        Assert.Equal(792 - 24 - 50, t);
        (l, t) = PdfSignPlacement.BlockOrigin(50, 50, 612, 792, 150, 50, 24);
        Assert.Equal(306 - 75, l, 3);
        Assert.Equal(396 - 25, t, 3);
        (l, _) = PdfSignPlacement.BlockOrigin(50, 50, 100, 100, 150, 50, 24);   // block wider than the page: centred
        Assert.Equal(-25, l, 3);
    }

    // ---- the real flow through the engines ----

    [Fact]
    public void Merge_in_the_strip_order_writes_the_pages_in_that_order()
    {
        var d = PdfKit.NewDir();
        var invoice = Doc(d, "invoice.pdf", 1, "INV");
        var contract = Doc(d, "contract.pdf", 12, "CON");
        var receipt = Doc(d, "receipt.pdf", 1, "REC");

        var strip = new List<string> { invoice, contract, receipt };
        PdfPagePlan.MoveBlock(strip, new[] { 2 }, 0);                     // drag the receipt to the front
        var merged = PdfKit.RunOk("pdf-merge", strip, PdfKit.NewDir("out"))[0];

        var lines = Lines(merged);
        Assert.Equal(14, lines.Length);
        Assert.Equal("REC-01", lines[0]);
        Assert.Equal("INV-01", lines[1]);
        Assert.Equal("CON-01", lines[2]);
        Assert.Equal("CON-12", lines[13]);
    }

    [Fact]
    public void The_whole_flow_reorder_delete_blank_rotate_then_sign()
    {
        var d = PdfKit.NewDir();
        var invoice = Doc(d, "invoice.pdf", 1, "INV");
        var contract = Doc(d, "contract.pdf", 12, "CON", blank: new[] { 4, 9 });   // two blank pages
        var receipt = Doc(d, "receipt.pdf", 1, "REC");

        var g = new PageGridModel(PageGridRules.Organize);
        g.Load(new[] { 1, 12, 1 });
        Assert.Equal(14, g.Pages.Count);

        // delete the two blank pages of the contract (grid index 1 + 3 and 1 + 8)
        g.Click(4, false, false);
        g.Click(9, false, true);
        Assert.Equal(2, g.DeleteSelected());

        // drag the receipt (last) to the front, rotate it
        g.ClearSelection();
        g.Click(11, false, false);
        Assert.True(g.MoveSelected(0));
        Assert.True(g.RotateSelected(90));

        var plan = g.PlanString();
        var organized = PdfKit.RunOk("pdf-organize", new[] { invoice, contract, receipt }, PdfKit.NewDir("out"), ("plan", plan))[0];

        var lines = Lines(organized);
        Assert.Equal(12, lines.Length);
        Assert.Equal("REC-01", lines[0]);
        Assert.Equal("INV-01", lines[1]);
        Assert.Equal("CON-01", lines[2]);
        Assert.DoesNotContain("CON-04", lines);
        Assert.DoesNotContain("CON-09", lines);
        Assert.Equal("CON-12", lines[11]);
        var rot = PdfKit.Rotations(organized);
        Assert.Equal(90, rot[0]);
        Assert.All(rot.Skip(1), r => Assert.Equal(0, r));

        var signed = PdfKit.RunOk("pdf-sign-self", new[] { organized }, PdfKit.NewDir("signed"), ("name", "Ada"), ("pages", "12"), ("at", "50,50"))[0];
        Assert.Equal(12, PdfKit.PageCount(signed));
        Assert.Contains("Ada", Lines(signed)[11]);
        Assert.DoesNotContain("Ada", Lines(signed)[0]);
    }

    [Fact]
    public void Remove_panel_spec_removes_exactly_those_pages()
    {
        var d = PdfKit.NewDir();
        var src = Doc(d, "five.pdf", 5, "FIVE");
        var g = new PageGridModel(PageGridRules.Remove);
        g.Load(new[] { 5 });
        g.Click(1, false, false);
        g.Click(3, false, true);
        g.DeleteSelected();
        var o = PdfKit.RunOk("pdf-remove-pages", new[] { src }, PdfKit.NewDir("out"), ("pages", g.RemovedSpec()))[0];
        Assert.Equal(new[] { "FIVE-01", "FIVE-03", "FIVE-05" }, Lines(o));
    }

    [Fact]
    public void Blank_insert_in_the_plan_adds_an_empty_page()
    {
        var d = PdfKit.NewDir();
        var a = Doc(d, "a.pdf", 2, "AA");
        var g = new PageGridModel(PageGridRules.Organize);
        g.Load(new[] { 2 });
        g.Click(0, false, false);
        g.InsertBlank();
        var o = PdfKit.RunOk("pdf-organize", new[] { a }, PdfKit.NewDir("out"), ("plan", g.PlanString()))[0];
        Assert.Equal(new[] { "AA-01", "", "AA-02" }, Lines(o));
    }

    // ---- where the signature lands ----

    [Theory]
    [InlineData("12,10", true, true)]
    [InlineData("88,90", false, false)]
    public void Sign_at_puts_the_mark_where_it_was_clicked(string at, bool left, bool top)
    {
        var d = PdfKit.NewDir();
        var src = PdfKit.Make(d, "p.pdf", 1, "PP");
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("o"), ("name", "Ada Lovelace"), ("at", at))[0];
        using var doc = Pig.PdfDocument.Open(o);
        var w = doc.GetPage(1).GetWords().First(x => x.Text == "Lovelace").BoundingBox;
        if (left) Assert.True(w.Centroid.X < 612 * 0.4); else Assert.True(w.Centroid.X > 612 * 0.6);
        if (top) Assert.True(w.Centroid.Y > 792 * 0.6); else Assert.True(w.Centroid.Y < 792 * 0.4);   // PDF space is y-up
    }

    [Fact]
    public void Sign_at_centres_the_mark_on_the_click()
    {
        var d = PdfKit.NewDir();
        var src = PdfKit.Make(d, "p.pdf", 1, "PP");
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("o"), ("name", "Ada"), ("at", "50,25"))[0];
        using var doc = Pig.PdfDocument.Open(o);
        var w = doc.GetPage(1).GetWords().First(x => x.Text == "Ada").BoundingBox;
        Assert.InRange(w.Centroid.X, 306 - 12, 306 + 12);
        Assert.InRange(w.Centroid.Y, 792 * 0.75 - 40, 792 * 0.75 + 40);
    }

    [Theory]
    [InlineData("50")]
    [InlineData("a,b")]
    [InlineData("101,5")]
    public void Sign_at_refuses_a_spot_that_is_not_on_the_page(string at)
    {
        var d = PdfKit.NewDir();
        var src = PdfKit.Make(d, "p.pdf", 1, "PP");
        var r = PdfKit.Run("pdf-sign-self", new[] { src }, PdfKit.NewDir("o"), ("name", "Ada"), ("at", at));
        Assert.False(r.Ok);
        Assert.Contains("'at'", r.Error);
    }
}
