using System.Text;
using PdfSharpCore.Pdf.IO;
using Pig = UglyToad.PdfPig;

namespace NetieSpace.Tests;

/// <summary>pdf-repair: damage is made here, from a real PDF; the result is judged by two independent readers.</summary>
public class PdfTool4RepairTests
{
    private static string Original(string dir) => PdfKit.Make(dir, "orig.pdf", 3, "ALPHA", extraLines: 4);

    private static string Damage(string dir, string original, string kind)
    {
        var b = File.ReadAllBytes(original);
        var s = Encoding.Latin1.GetString(b);
        byte[] outBytes;
        switch (kind)
        {
            case "truncated":
                outBytes = b[..(s.LastIndexOf("xref", s.LastIndexOf("startxref", StringComparison.Ordinal) - 1, StringComparison.Ordinal))];
                break;
            case "xref-offset":
            {
                var sx = s.LastIndexOf("startxref", StringComparison.Ordinal);
                var eof = s.IndexOf("%%EOF", sx, StringComparison.Ordinal);
                outBytes = Encoding.Latin1.GetBytes(s[..sx] + "startxref\n7\n" + s[eof..]);
                break;
            }
            case "garbage-after-eof":
                outBytes = b.Concat(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("JUNK-AFTER-EOF-0123456789\n", 40)) + "startxref\n987654\n%%EOF\n")).ToArray();
                break;
            case "no-trailer":
            {
                var t = s.LastIndexOf("trailer", StringComparison.Ordinal);
                outBytes = Encoding.Latin1.GetBytes(s[..t] + "trXXler" + s[(t + 7)..]);
                break;
            }
            default: throw new ArgumentException(kind);
        }
        var path = Path.Combine(dir, kind + ".pdf");
        File.WriteAllBytes(path, outBytes);
        return path;
    }

    private static bool PdfSharpOpens(string path)
    {
        try
        {
            using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            return d.PageCount > 0;
        }
        catch (Exception) { return false; }
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("xref-offset")]
    [InlineData("garbage-after-eof")]
    [InlineData("no-trailer")]
    public void Damaged_file_is_refused_by_PdfSharp_and_repaired_into_a_file_both_readers_open(string kind)
    {
        var dir = PdfKit.NewDir();
        var orig = Original(dir);
        var expected = PdfKit.Texts(orig);
        var bad = Damage(dir, orig, kind);
        Assert.False(PdfSharpOpens(bad), "the damage must be one PdfSharpCore refuses: " + kind);

        var outDir = PdfKit.NewDir("out");
        var outputs = PdfKit.RunOk("pdf-repair", new[] { bad }, outDir);

        var fixedPath = Assert.Single(outputs);
        Assert.StartsWith(outDir, fixedPath);
        Assert.True(PdfSharpOpens(fixedPath));
        Assert.Equal(3, PdfKit.PageCount(fixedPath));
        Assert.Equal(expected, PdfKit.Texts(fixedPath));
        Assert.Contains("ALPHA page 2", PdfKit.Texts(fixedPath)[1]);
        Assert.Equal(PdfKit.Bytes(bad), File.ReadAllBytes(bad));
    }

    [Fact]
    public void Repair_keeps_title_and_author()
    {
        var dir = PdfKit.NewDir();
        var orig = PdfKit.Make(dir, "titled.pdf", 2, "TT", tweak: null);
        using (var d = PdfReader.Open(orig, PdfDocumentOpenMode.Modify))
        {
            d.Info.Title = "Quarterly";
            d.Info.Author = "Ada";
            d.Save(orig);
        }
        var bad = Damage(dir, orig, "no-trailer");
        var o = PdfKit.RunOk("pdf-repair", new[] { bad }, PdfKit.NewDir("out"))[0];
        using var pig = Pig.PdfDocument.Open(o);
        Assert.Equal("Quarterly", pig.Information.Title);
        Assert.Equal("Ada", pig.Information.Author);
    }

    [Fact]
    public void Undamaged_file_is_refused_as_nothing_to_repair_and_no_file_is_written()
    {
        var dir = PdfKit.NewDir();
        var good = Original(dir);
        var outDir = PdfKit.NewDir("out");
        var r = PdfKit.Run("pdf-repair", new[] { good }, outDir);
        Assert.False(r.Ok);
        Assert.Contains("nothing to repair", r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Theory]
    [InlineData("random")]
    [InlineData("header-only")]
    [InlineData("empty")]
    [InlineData("text-not-pdf")]
    public void Nothing_recoverable_fails_with_a_clear_message_and_writes_nothing(string kind)
    {
        var dir = PdfKit.NewDir();
        var path = Path.Combine(dir, kind + ".pdf");
        byte[] content = kind switch
        {
            "random" => RandomBytes(4000),
            "header-only" => Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"),
            "empty" => Array.Empty<byte>(),
            _ => Encoding.ASCII.GetBytes("hello, this is a text file\n"),
        };
        File.WriteAllBytes(path, content);
        var outDir = PdfKit.NewDir("out");
        var r = PdfKit.Run("pdf-repair", new[] { path }, outDir);
        Assert.False(r.Ok);
        Assert.Contains("nothing could be recovered", r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        new Random(5).NextBytes(b);
        return b;
    }

    [Fact]
    public void Pages_that_only_have_empty_content_never_become_a_blank_pdf()
    {
        // A damaged file whose single page object survives but whose content stream is gone.
        var dir = PdfKit.NewDir();
        var orig = PdfKit.Make(dir, "one.pdf", 1, "GONE");
        var s = Encoding.Latin1.GetString(File.ReadAllBytes(orig));
        var cut = s[..s.LastIndexOf("xref", s.LastIndexOf("startxref", StringComparison.Ordinal) - 1, StringComparison.Ordinal)];
        var bad = Path.Combine(dir, "nocontent.pdf");
        File.WriteAllText(bad, System.Text.RegularExpressions.Regex.Replace(cut, @"/Contents \d+ 0 R", ""), Encoding.Latin1);
        Assert.False(PdfSharpOpens(bad));
        var outDir = PdfKit.NewDir("out");
        var r = PdfKit.Run("pdf-repair", new[] { bad }, outDir);
        Assert.False(r.Ok, "a page with no content must not be copied as a blank page: " + r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Two_files_in_one_run_one_output_each_and_inputs_untouched()
    {
        var dir = PdfKit.NewDir();
        var a = Damage(dir, Original(dir), "no-trailer");
        var d2 = PdfKit.NewDir("b");
        var b = Damage(d2, PdfKit.Make(d2, "orig.pdf", 2, "BETA"), "garbage-after-eof");
        var before = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        var outDir = PdfKit.NewDir("out");
        var outs = PdfKit.RunOk("pdf-repair", new[] { a, b }, outDir);
        Assert.Equal(2, outs.Count);
        Assert.Equal(3, PdfKit.PageCount(outs[0]));
        Assert.Equal(2, PdfKit.PageCount(outs[1]));
        Assert.Equal(before.Item1, File.ReadAllBytes(a));
        Assert.Equal(before.Item2, File.ReadAllBytes(b));
    }

    [Fact]
    public void Second_run_into_the_same_folder_never_overwrites()
    {
        var dir = PdfKit.NewDir();
        var bad = Damage(dir, Original(dir), "no-trailer");
        var outDir = PdfKit.NewDir("out");
        var first = PdfKit.RunOk("pdf-repair", new[] { bad }, outDir)[0];
        var second = PdfKit.RunOk("pdf-repair", new[] { bad }, outDir)[0];
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
    }

    [Fact]
    public void Cancelled_run_leaves_nothing_behind()
    {
        var dir = PdfKit.NewDir();
        var bad = Damage(dir, Original(dir), "no-trailer");
        var outDir = PdfKit.NewDir("out");
        var tool = PdfKit.Registry.Find("pdf-repair")!;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = NetieSpace.Apps.Tools.ToolRunner.RunAsync(tool,
            new NetieSpace.Apps.Tools.ToolRequest(new[] { bad }, outDir, new Dictionary<string, string>()), null, cts.Token).GetAwaiter().GetResult();
        Assert.False(r.Ok);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Bad_option_value_names_the_option()
    {
        var dir = PdfKit.NewDir();
        var bad = Damage(dir, Original(dir), "no-trailer");
        var r = PdfKit.Run("pdf-repair", new[] { bad }, PdfKit.NewDir("out"), ("partial", "maybe"));
        Assert.False(r.Ok);
        Assert.Contains("option 'partial'", r.Error);
    }

    [Fact]
    public void Page_that_lost_its_content_is_named_and_dropped_only_with_partial()
    {
        var dir = PdfKit.NewDir();
        var orig = Original(dir);
        var expected = PdfKit.Texts(orig);
        var bad = Damage(dir, orig, "no-trailer");
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(bad));
        var pageObj = System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?![a-zA-Z])")[2];
        var damaged = Path.Combine(dir, "lost-page.pdf");
        File.WriteAllText(damaged, text[..pageObj.Index] + RemoveFirst(text[pageObj.Index..]), Encoding.Latin1);

        var strict = PdfKit.Run("pdf-repair", new[] { damaged }, PdfKit.NewDir("out1"));
        Assert.False(strict.Ok);
        Assert.Contains("could not be read or show nothing", strict.Error);
        Assert.Contains("'partial'", strict.Error);

        var outDir = PdfKit.NewDir("out2");
        var o = PdfKit.RunOk("pdf-repair", new[] { damaged }, outDir, ("partial", "true"))[0];
        var got = PdfKit.Texts(o);
        Assert.Equal(2, got.Count);
        Assert.All(got, t => Assert.Contains(t, expected));
    }

    private static string RemoveFirst(string s) =>
        new System.Text.RegularExpressions.Regex(@"/Contents\s+\d+\s+\d+\s+R").Replace(s, "", 1);

    [Fact]
    public void The_fixture_used_by_the_Windows_check_is_repaired_the_same_way()
    {
        // scripts/checks.d/pdf4.ps1 cuts tests/fixtures/preview/Q3_Report.pdf just before its xref table.
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "tests", "fixtures", "preview", "Q3_Report.pdf")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var fixture = Path.Combine(root!, "tests", "fixtures", "preview", "Q3_Report.pdf");
        var bytes = File.ReadAllBytes(fixture);
        var s = Encoding.Latin1.GetString(bytes);
        var cut = s.LastIndexOf("xref", s.LastIndexOf("startxref", StringComparison.Ordinal) - 1, StringComparison.Ordinal);
        var dir = PdfKit.NewDir();
        var bad = Path.Combine(dir, "Q3_damaged.pdf");
        File.WriteAllBytes(bad, bytes[..cut]);
        Assert.False(PdfSharpOpens(bad));
        var o = PdfKit.RunOk("pdf-repair", new[] { bad }, PdfKit.NewDir("out"))[0];
        Assert.True(PdfSharpOpens(o));
        Assert.Equal(PdfKit.Texts(fixture), PdfKit.Texts(o));
        Assert.Equal(PdfKit.PageCount(fixture), PdfKit.PageCount(o));
    }

    [Fact]
    public void A_stray_catalog_without_pages_does_not_hide_the_real_one()
    {
        var dir = PdfKit.NewDir();
        var orig = Original(dir);
        var bad = Damage(dir, orig, "truncated");
        File.AppendAllText(bad, "\n99 0 obj\n<< /Type /Catalog >>\nendobj\n", Encoding.Latin1);
        var o = PdfKit.RunOk("pdf-repair", new[] { bad }, PdfKit.NewDir("out"))[0];
        Assert.Equal(PdfKit.Texts(orig), PdfKit.Texts(o));
    }
}
