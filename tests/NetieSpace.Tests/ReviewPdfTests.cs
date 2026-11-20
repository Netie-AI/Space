using System.Text;
using System.Text.RegularExpressions;
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Annotations;

namespace NetieSpace.Tests;

/// <summary>Review fixes for the PDF tools (S50 wave 4). Each test was written red first.</summary>
public class ReviewPdfTests
{
    /// <summary>Number of /Type /Page objects in a file (not /Pages): what the file really carries.</summary>
    private static int PageObjects(string path) =>
        Regex.Matches(PdfKit.Latin1(path), @"/Type\s*/Page(?![A-Za-z])").Count;

    /// <summary>Four pages. Page 1 links to pages 2 (a /Dest link) and 3 and 4 (/A /GoTo actions).</summary>
    private static string LinkedPdf(string dir, string name = "linked.pdf", Action<PdfDocument>? tweak = null)
    {
        using var doc = new PdfDocument();
        for (var i = 1; i <= 4; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            g.DrawString($"LINKED page {i}", new XFont("Arial", 14), XBrushes.Black, new XPoint(60, 100));
        }
        var first = doc.Pages[0];
        first.AddDocumentLink(new PdfRectangle(new XRect(50, 80, 100, 30)), 2);
        foreach (var target in new[] { 2, 3 })
        {
            var action = new PdfDictionary(doc);
            action.Elements.SetName("/S", "/GoTo");
            var dest = new PdfArray(doc);
            dest.Elements.Add(doc.Pages[target].Reference!);
            dest.Elements.Add(new PdfName("/Fit"));
            action.Elements["/D"] = dest;
            var annot = new PdfDictionary(doc);
            annot.Elements.SetName("/Type", "/Annot");
            annot.Elements.SetName("/Subtype", "/Link");
            annot.Elements["/Rect"] = new PdfRectangle(new XRect(50, 120 + target * 10, 100, 20));
            annot.Elements["/A"] = action;
            doc.Internals.AddObject(annot);
            first.Annotations.Elements.Add(annot);
        }
        tweak?.Invoke(doc);
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    [Fact]
    public void The_linked_fixture_really_has_four_pages_and_three_links()
    {
        var src = LinkedPdf(PdfKit.NewDir());
        Assert.Equal(4, PdfKit.PageCount(src));
        Assert.Equal(4, PageObjects(src));
        Assert.Equal(3, Regex.Matches(PdfKit.Latin1(src), @"/Subtype\s*/Link").Count);
    }

    [Theory]
    [InlineData("pdf-extract-pages", "pages|1", 1)]
    [InlineData("pdf-extract-pages", "pages|1&merge|false", 1)]
    [InlineData("pdf-remove-pages", "pages|2-4", 1)]
    [InlineData("pdf-split", "split_mode|ranges&ranges|1", 1)]
    [InlineData("pdf-split", "split_mode|remove_pages&ranges|2-4", 1)]
    [InlineData("pdf-organize", "plan|1:1", 1)]
    [InlineData("pdf-organize", "plan|1:1;1:1", 2)]
    public void Link_annotations_do_not_drag_dropped_pages_into_the_output(string tool, string optionPairs, int expectedPages)
    {
        var dir = PdfKit.NewDir();
        var src = LinkedPdf(dir);
        var opts = optionPairs.Split("&").Select(p => p.Split('|')).Select(p => (p[0], p[1])).ToArray();

        var outputs = PdfKit.RunOk(tool, new[] { src }, PdfKit.NewDir(), opts);

        Assert.NotEmpty(outputs);
        foreach (var o in outputs)
            Assert.Equal(PdfKit.PageCount(o), PageObjects(o));
        Assert.Equal(expectedPages, PdfKit.PageCount(outputs[0]));
        Assert.Equal(expectedPages, PageObjects(outputs[0]));
        Assert.Contains("LINKED page 1", string.Join(" ", PdfKit.Texts(outputs[0])));
    }

    [Fact]
    public void Link_annotations_do_not_drag_dropped_pages_into_a_fixed_range_split()
    {
        var src = LinkedPdf(PdfKit.NewDir());
        var outputs = PdfKit.RunOk("pdf-split", new[] { src }, PdfKit.NewDir(), ("split_mode", "fixed_range"), ("every", "1"));
        Assert.Equal(4, outputs.Count);
        foreach (var o in outputs)
        {
            Assert.Equal(1, PdfKit.PageCount(o));
            Assert.Equal(1, PageObjects(o));
        }
    }

    [Fact]
    public void Link_annotations_do_not_drag_other_pages_into_an_unlock()
    {
        var dir = PdfKit.NewDir();
        var src = LinkedPdf(dir, "locked.pdf", doc =>
        {
            doc.SecuritySettings.UserPassword = "user-pw";
            doc.SecuritySettings.OwnerPassword = "owner-pw";
        });

        var outputs = PdfKit.RunOk("pdf-unlock", new[] { src }, PdfKit.NewDir(), ("password", "user-pw"));

        Assert.Equal(4, PdfKit.PageCount(outputs[0]));
        Assert.Equal(4, PageObjects(outputs[0]));
    }

    [Fact]
    public void Merge_does_not_double_the_pages_a_link_points_at()
    {
        var dir = PdfKit.NewDir();
        var one = LinkedPdf(dir, "one.pdf");
        var two = LinkedPdf(dir, "two.pdf");
        var outputs = PdfKit.RunOk("pdf-merge", new[] { one, two }, PdfKit.NewDir());
        Assert.Equal(8, PdfKit.PageCount(outputs[0]));
        Assert.Equal(8, PageObjects(outputs[0]));
    }

    [Fact]
    public void Extract_text_never_overwrites_an_existing_txt()
    {
        var outDir = PdfKit.NewDir();
        var mine = IoPath.Combine(outDir, "Q3_Report.txt");
        IoFile.WriteAllText(mine, "my own notes");

        var r = PdfKit.Run("pdf-extract-text", new[] { ToolTestSupport.FixturePdf() }, outDir);

        Assert.True(r.Ok, r.Error);
        Assert.Equal("my own notes", IoFile.ReadAllText(mine));
        var output = Assert.Single(r.Outputs);
        Assert.NotEqual(mine, output);
        Assert.EndsWith(".txt", output);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public void Diff_memory_does_not_grow_with_the_square_of_the_line_count()
    {
        var a = Enumerable.Range(0, 2000).Select(i => "before " + i).ToList();
        var b = Enumerable.Range(0, 2000).Select(i => "after " + i).ToList();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var ops = PdfTextTools.Diff(a, b, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 40L * 1024 * 1024, $"diff of 2000 vs 2000 different lines allocated {allocated / (1024 * 1024)} MB");
        // Past the edit-distance cap the middle is reported as replaced: every old line out, every new line in.
        Assert.Equal(2000, ops.Count(o => o.Kind == '-'));
        Assert.Equal(2000, ops.Count(o => o.Kind == '+'));
        Assert.Equal(0, ops.Count(o => o.Kind == '='));
    }

    [Fact]
    public void Diff_of_large_files_with_few_edits_is_cheap_and_exact()
    {
        var a = Enumerable.Range(0, 30000).Select(i => "line " + i).ToList();
        var b = new List<string>(a);
        b.RemoveAt(12000);
        b.Insert(20000, "inserted");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var ops = PdfTextTools.Diff(a, b, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 16L * 1024 * 1024, $"allocated {allocated / (1024 * 1024)} MB");
        Assert.Equal(new[] { '-', '+' }, ops.Where(o => o.Kind != '=').Select(o => o.Kind).ToArray());
        Assert.Equal(12000, ops.Single(o => o.Kind == '-').Index);
        Assert.Equal(20000, ops.Single(o => o.Kind == '+').Index);
    }

    [Fact]
    public void Diff_replays_to_the_new_text_and_is_minimal_on_random_edits()
    {
        var rnd = new Random(7);
        for (var round = 0; round < 60; round++)
        {
            var a = Enumerable.Range(0, rnd.Next(0, 40)).Select(_ => "l" + rnd.Next(0, 8)).ToList();
            var b = new List<string>(a);
            for (var e = rnd.Next(0, 12); e > 0; e--)
            {
                if (b.Count > 0 && rnd.Next(2) == 0) b.RemoveAt(rnd.Next(b.Count));
                else b.Insert(rnd.Next(b.Count + 1), "l" + rnd.Next(0, 8));
            }

            var ops = PdfTextTools.Diff(a, b, CancellationToken.None);

            var replay = new List<string>();
            foreach (var o in ops)
            {
                if (o.Kind == '=') { Assert.Equal(a[o.Index], a[o.Index]); replay.Add(a[o.Index]); }
                else if (o.Kind == '+') replay.Add(b[o.Index]);
            }
            Assert.Equal(b, replay);
            // Every old line is either kept or removed exactly once, in order.
            Assert.Equal(Enumerable.Range(0, a.Count), ops.Where(o => o.Kind != '+').Select(o => o.Index));

            var lcs = new int[a.Count + 1, b.Count + 1];
            for (var i = a.Count - 1; i >= 0; i--)
                for (var j = b.Count - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            Assert.Equal(a.Count + b.Count - 2 * lcs[0, 0], ops.Count(o => o.Kind != '='));
        }
    }
}
