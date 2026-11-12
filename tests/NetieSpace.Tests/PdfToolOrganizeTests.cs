using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Tests;

public class PdfToolOrganizeTests
{
    private static void AssertPages(string path, string tag, params int[] pageNumbers)
    {
        var t = PdfKit.Texts(path);
        Assert.Equal(pageNumbers.Length, t.Count);
        for (var i = 0; i < pageNumbers.Length; i++)
            Assert.Contains($"{tag} page {pageNumbers[i]}", t[i]);
    }

    // ----- merge -----

    [Fact]
    public void Merge_joins_inputs_in_the_order_given()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "AAA");
        var b = PdfKit.Make(dir, "b.pdf", 3, "BBB");
        var outDir = PdfKit.NewDir();

        var outs = PdfKit.RunOk("pdf-merge", new[] { b, a }, outDir);

        var single = Assert.Single(outs);
        Assert.StartsWith(outDir, single);
        Assert.Equal(5, PdfKit.PageCount(single));
        var t = PdfKit.Texts(single);
        Assert.Contains("BBB page 1", t[0]);
        Assert.Contains("BBB page 3", t[2]);
        Assert.Contains("AAA page 1", t[3]);
        Assert.Contains("AAA page 2", t[4]);
    }

    [Fact]
    public void Merge_refuses_one_file_and_a_damaged_file_names_the_problem()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 1);
        var bad = IoPath.Combine(dir, "bad.pdf");
        IoFile.WriteAllText(bad, "this is not a pdf at all");
        var outDir = PdfKit.NewDir();

        var one = PdfKit.Run("pdf-merge", new[] { a }, outDir);
        Assert.False(one.Ok);
        Assert.Contains("at least two", one.Error);

        var broken = PdfKit.Run("pdf-merge", new[] { a, bad }, outDir);
        Assert.False(broken.Ok);
        Assert.Contains("bad.pdf", broken.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Merge_runs_through_the_tool_cli()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 1, "AAA");
        var b = PdfKit.Make(dir, "b.pdf", 1, "BBB");
        var outDir = PdfKit.NewDir();

        var (exit, stdout, err) = PdfKit.Cli("pdf-merge", new[] { a, b }, outDir);

        Assert.True(exit == 0, err);
        var path = Assert.Single(PdfKit.Lines(stdout));
        Assert.Equal(2, PdfKit.PageCount(path));
    }

    // ----- split -----

    [Fact]
    public void Split_ranges_writes_one_file_per_range()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "book.pdf", 6, "BK");
        var outDir = PdfKit.NewDir();

        var outs = PdfKit.RunOk("pdf-split", new[] { src }, outDir, ("split_mode", "ranges"), ("ranges", "1-2,4,-2-end"));

        Assert.Equal(3, outs.Count);
        AssertPages(outs[0], "BK", 1, 2);
        AssertPages(outs[1], "BK", 4);
        AssertPages(outs[2], "BK", 5, 6);
    }

    [Fact]
    public void Split_fixed_range_cuts_every_n_pages_with_a_short_tail()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "book.pdf", 5, "BK");

        var outs = PdfKit.RunOk("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "fixed_range"), ("every", "2"));

        Assert.Equal(3, outs.Count);
        AssertPages(outs[0], "BK", 1, 2);
        AssertPages(outs[1], "BK", 3, 4);
        AssertPages(outs[2], "BK", 5);
    }

    [Fact]
    public void Split_remove_pages_mode_keeps_the_rest_in_one_file()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "book.pdf", 4, "BK");

        var outs = PdfKit.RunOk("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "remove_pages"), ("ranges", "2-3"));

        AssertPages(Assert.Single(outs), "BK", 1, 4);
    }

    [Fact]
    public void Split_filesize_keeps_every_part_under_the_limit_and_loses_no_page()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "heavy.pdf", 8, "HV", extraLines: 40);
        var onePage = PdfKit.RunOk("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "fixed_range"), ("every", "1"));
        var biggestSingle = onePage.Max(p => new FileInfo(p).Length);
        var limitKb = (int)(biggestSingle * 1.25 / 1024) + 1;

        var outs = PdfKit.RunOk("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "filesize"), ("max_size_kb", limitKb.ToString()));

        Assert.True(outs.Count > 1 && outs.Count < 8, "expected a grouping, got " + outs.Count + " parts");
        Assert.All(outs, p => Assert.True(new FileInfo(p).Length <= limitKb * 1024L, p));
        var all = outs.SelectMany(p => PdfKit.Texts(p)).ToList();
        Assert.Equal(8, all.Count);
        for (var i = 0; i < 8; i++) Assert.Contains($"HV page {i + 1}", all[i]);
    }

    [Fact]
    public void Split_filesize_names_the_page_that_cannot_fit()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "heavy.pdf", 2, "HV", extraLines: 40);

        var r = PdfKit.Run("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "filesize"), ("max_size_kb", "1"));

        Assert.False(r.Ok);
        Assert.Contains("page 1", r.Error);
        Assert.Contains("max_size_kb", r.Error);
    }

    [Fact]
    public void Split_rejects_a_missing_or_out_of_range_ranges_option_by_name()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 3);
        var none = PdfKit.Run("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "ranges"));
        var far = PdfKit.Run("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "ranges"), ("ranges", "2-9"));
        Assert.False(none.Ok);
        Assert.Contains("ranges", none.Error);
        Assert.False(far.Ok);
        Assert.Contains("'ranges'", far.Error);
        Assert.Contains("1..3", far.Error);
    }

    [Fact]
    public void Split_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 3, "BK");
        var outDir = PdfKit.NewDir();
        var (exit, stdout, err) = PdfKit.Cli("pdf-split", new[] { src }, outDir, ("ranges", "1,2-3"));
        Assert.True(exit == 0, err);
        var lines = PdfKit.Lines(stdout);
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, PdfKit.PageCount(lines[0]));
        Assert.Equal(2, PdfKit.PageCount(lines[1]));
    }

    // ----- remove pages -----

    [Fact]
    public void Remove_pages_drops_exactly_the_named_pages()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 6, "BK");
        var outs = PdfKit.RunOk("pdf-remove-pages", new[] { src }, PdfKit.NewDir(), ("pages", "2,4-5"));
        AssertPages(Assert.Single(outs), "BK", 1, 3, 6);
    }

    [Fact]
    public void Remove_pages_refuses_removing_everything_and_bad_pages_naming_the_option()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 3);
        var all = PdfKit.Run("pdf-remove-pages", new[] { src }, PdfKit.NewDir(), ("pages", "1-end"));
        var past = PdfKit.Run("pdf-remove-pages", new[] { src }, PdfKit.NewDir(), ("pages", "4"));
        var none = PdfKit.Run("pdf-remove-pages", new[] { src }, PdfKit.NewDir());
        Assert.False(all.Ok); Assert.Contains("'pages'", all.Error);
        Assert.False(past.Ok); Assert.Contains("'pages'", past.Error);
        Assert.False(none.Ok); Assert.Contains("'pages'", none.Error);
    }

    [Fact]
    public void Remove_pages_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 3, "BK");
        var (exit, stdout, err) = PdfKit.Cli("pdf-remove-pages", new[] { src }, PdfKit.NewDir(), ("pages", "1"));
        Assert.True(exit == 0, err);
        AssertPages(Assert.Single(PdfKit.Lines(stdout)), "BK", 2, 3);
    }

    // ----- extract pages -----

    [Fact]
    public void Extract_pages_merged_keeps_the_requested_order_and_supports_from_the_end()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 6, "BK");
        var outs = PdfKit.RunOk("pdf-extract-pages", new[] { src }, PdfKit.NewDir(), ("pages", "5,1-2,-1"));
        AssertPages(Assert.Single(outs), "BK", 5, 1, 2, 6);
    }

    [Fact]
    public void Extract_pages_unmerged_writes_one_file_per_page()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 4, "BK");
        var outs = PdfKit.RunOk("pdf-extract-pages", new[] { src }, PdfKit.NewDir(), ("pages", "2-3"), ("merge", "false"));
        Assert.Equal(2, outs.Count);
        AssertPages(outs[0], "BK", 2);
        AssertPages(outs[1], "BK", 3);
    }

    [Fact]
    public void Extract_pages_runs_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "book.pdf", 4, "BK");
        var (exit, stdout, err) = PdfKit.Cli("pdf-extract-pages", new[] { src }, PdfKit.NewDir(), ("pages", "3-4"));
        Assert.True(exit == 0, err);
        AssertPages(Assert.Single(PdfKit.Lines(stdout)), "BK", 3, 4);
    }

    // ----- organize -----

    [Fact]
    public void Organize_follows_the_plan_across_files_with_rotation_and_a_blank_page()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 3, "AAA");
        var b = PdfKit.Make(dir, "b.pdf", 2, "BBB", width: 400, height: 300);

        var outs = PdfKit.RunOk("pdf-organize", new[] { a, b }, PdfKit.NewDir(), ("plan", "2:2;blank;1:3,1@90"));

        var path = Assert.Single(outs);
        var t = PdfKit.Texts(path);
        Assert.Equal(4, t.Count);
        Assert.Contains("BBB page 2", t[0]);
        Assert.Equal("", t[1].Trim());
        Assert.Contains("AAA page 3", t[2]);
        Assert.Contains("AAA page 1", t[3]);
        Assert.Equal(new[] { 0, 0, 90, 90 }, PdfKit.Rotations(path));
    }

    [Fact]
    public void Organize_blank_page_takes_the_size_of_the_page_before_it_and_rotation_is_applied()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "AAA");
        var b = PdfKit.Make(dir, "b.pdf", 1, "BBB", width: 400, height: 300);

        var path = Assert.Single(PdfKit.RunOk("pdf-organize", new[] { a, b }, PdfKit.NewDir(), ("plan", "2:1;blank;1:1@90;1:2")));

        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.Equal(4, d.PageCount);
        Assert.Equal(400, d.Pages[1].Width.Point, 1);
        Assert.Equal(300, d.Pages[1].Height.Point, 1);
        Assert.Equal(new[] { 0, 0, 90, 0 }, Enumerable.Range(0, 4).Select(i => d.Pages[i].Rotate).ToArray());
    }

    [Fact]
    public void Organize_without_a_plan_keeps_every_page_of_every_file_in_order_and_bad_plans_name_the_option()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "AAA");
        var b = PdfKit.Make(dir, "b.pdf", 1, "BBB");
        var all = PdfKit.Texts(Assert.Single(PdfKit.RunOk("pdf-organize", new[] { a, b }, PdfKit.NewDir())));
        Assert.Equal(3, all.Count);
        Assert.Contains("AAA page 1", all[0]);
        Assert.Contains("AAA page 2", all[1]);
        Assert.Contains("BBB page 1", all[2]);

        var badFile = PdfKit.Run("pdf-organize", new[] { a }, PdfKit.NewDir(), ("plan", "3:1"));
        var badRot = PdfKit.Run("pdf-organize", new[] { a }, PdfKit.NewDir(), ("plan", "1:1@45"));
        var junk = PdfKit.Run("pdf-organize", new[] { a }, PdfKit.NewDir(), ("plan", "hello"));
        Assert.All(new[] { badFile, badRot, junk }, r => { Assert.False(r.Ok); Assert.Contains("'plan'", r.Error); });
    }

    [Fact]
    public void Organize_runs_through_the_tool_cli()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(dir, "a.pdf", 2, "AAA");
        var (exit, stdout, err) = PdfKit.Cli("pdf-organize", new[] { a }, PdfKit.NewDir(), ("plan", "1:2;1:1"));
        Assert.True(exit == 0, err);
        AssertPages(Assert.Single(PdfKit.Lines(stdout)), "AAA", 2, 1);
    }
}
