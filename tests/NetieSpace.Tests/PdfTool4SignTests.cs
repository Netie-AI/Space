using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using NetieSpace.Services;
using PdfSharpCore.Pdf.IO;
using Pig = UglyToad.PdfPig;

namespace NetieSpace.Tests;

/// <summary>pdf-sign-self: the visible mark is judged by reading the result back, the certificate step by verifying it.</summary>
public class PdfTool4SignTests
{
    private const string Name = "Ada Lovelace";

    private static string ThreePages(string dir) =>
        PdfKit.Make(dir, "contract.pdf", 3, "CONTRACT", tweak: (p, i) =>
        {
            if (i == 2) { p.Width = 792; p.Height = 612; }
            if (i == 3) { p.Width = 595; p.Height = 842; }
        });

    private static List<(double W, double H)> Sizes(string path)
    {
        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, d.PageCount).Select(i => (d.Pages[i].Width.Point, d.Pages[i].Height.Point)).ToList();
    }

    private static Pig.Content.Word? WordOn(string path, int page, string word)
    {
        using var d = Pig.PdfDocument.Open(path);
        return d.GetPage(page).GetWords().FirstOrDefault(w => w.Text == word);
    }

    private static string PfxFile(string dir, string password, string cn = "Ada Lovelace")
    {
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned(cn);
        var path = Path.Combine(dir, "id.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }

    [Fact]
    public void Typed_name_lands_on_the_chosen_page_only_and_nothing_else_changes()
    {
        var dir = PdfKit.NewDir();
        var src = ThreePages(dir);
        var before = PdfKit.Texts(src);
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"), ("name", Name), ("pages", "3"))[0];

        var after = PdfKit.Texts(o);
        Assert.Equal(3, after.Count);
        Assert.Contains("Lovelace", after[2]);
        Assert.DoesNotContain("Lovelace", after[0]);
        Assert.DoesNotContain("Lovelace", after[1]);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[1], after[1]);
        Assert.StartsWith(before[2], after[2]);
        Assert.Equal(Sizes(src), Sizes(o));
        Assert.Equal(3, PdfKit.PageCount(o));
    }

    [Fact]
    public void Page_list_marks_each_listed_page_and_not_the_rest()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "five.pdf", 5, "FIVE");
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"), ("name", Name), ("pages", "1,4-5"))[0];
        var t = PdfKit.Texts(o);
        Assert.Equal(new[] { true, false, false, true, true }, t.Select(x => x.Contains("Lovelace")).ToArray());
    }

    [Fact]
    public void Default_page_is_the_last_one()
    {
        var dir = PdfKit.NewDir();
        var o = PdfKit.RunOk("pdf-sign-self", new[] { ThreePages(dir) }, PdfKit.NewDir("out"), ("name", Name))[0];
        var t = PdfKit.Texts(o);
        Assert.Equal(new[] { false, false, true }, t.Select(x => x.Contains("Lovelace")).ToArray());
    }

    [Fact]
    public void Date_line_is_todays_date_and_only_on_marked_pages()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "d.pdf", 2, "DD");
        var plain = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("o1"), ("name", Name), ("pages", "2"))[0];
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", PdfKit.Texts(plain)[1]);
        var dated = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("o2"), ("name", Name), ("pages", "2"), ("date", "true"))[0];
        var t = PdfKit.Texts(dated);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}", t[1]);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", t[0]);
        var m = Regex.Match(t[1], @"\d{4}-\d{2}-\d{2}");
        Assert.True(Math.Abs((DateTime.Parse(m.Value) - DateTime.Now.Date).TotalDays) <= 1);
    }

    [Fact]
    public void Grid_position_and_size_are_honoured()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "g.pdf", 1, "GG");
        string Run(string pos, string size, string tag) =>
            PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir(tag), ("name", Name), ("position", pos), ("size", size))[0];

        var topLeft = WordOn(Run("top-left", "medium", "a"), 1, "Lovelace")!.BoundingBox;
        var bottomRight = WordOn(Run("bottom-right", "medium", "b"), 1, "Lovelace")!.BoundingBox;
        var middle = WordOn(Run("middle-center", "medium", "c"), 1, "Lovelace")!.BoundingBox;
        Assert.True(topLeft.Left < bottomRight.Left);
        Assert.True(topLeft.Bottom > bottomRight.Bottom);   // PDF space is y-up: top has the larger y
        Assert.InRange(middle.Centroid.X, 612 / 2.0 - 120, 612 / 2.0 + 120);
        Assert.InRange(middle.Centroid.Y, 792 / 2.0 - 40, 792 / 2.0 + 40);

        var small = WordOn(Run("bottom-right", "small", "d"), 1, "Lovelace")!.BoundingBox.Width;
        var large = WordOn(Run("bottom-right", "large", "e"), 1, "Lovelace")!.BoundingBox.Width;
        Assert.True(large > small * 1.5, $"large {large} vs small {small}");
        Assert.True(WordOn(Run("bottom-right", "large", "f"), 1, "Lovelace")!.BoundingBox.Right <= 612 - 20);
    }

    [Fact]
    public void Long_name_still_fits_inside_the_page()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "l.pdf", 1, "LL");
        var longName = "Bartholomew Alexander Montgomery-Featherstonehaugh III";
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"), ("name", longName), ("position", "bottom-right"))[0];
        using var d = Pig.PdfDocument.Open(o);
        var words = d.GetPage(1).GetWords().Where(x => longName.Contains(x.Text) || x.Text == "III").ToList();
        Assert.Contains(words, x => x.Text == "III");
        Assert.All(words, x => { Assert.InRange(x.BoundingBox.Left, 0, 612); Assert.InRange(x.BoundingBox.Right, 0, 612 - 20); });
        Assert.InRange(words.Min(x => x.BoundingBox.Left), 24 - 1, 612);
    }

    [Fact]
    public void A_name_that_cannot_fit_across_the_page_is_refused()
    {
        var dir = PdfKit.NewDir();
        var outDir = PdfKit.NewDir("out");
        var r = PdfKit.Run("pdf-sign-self", new[] { PdfKit.Make(dir, "l.pdf", 1) }, outDir, ("name", new string('W', 200)));
        Assert.False(r.Ok);
        Assert.Contains("option 'name'", r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public void Picture_signature_is_one_extra_image_on_the_marked_pages_only(string kind)
    {
        var dir = PdfKit.NewDir();
        var src = ThreePages(dir);
        var pic = kind == "png" ? PdfTool3Kit.PhotoPng(dir, "sig.png", 300, 100, alpha: true) : PdfTool3Kit.PhotoJpeg(dir, "sig.jpg", 300, 100);
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"),
            ("mode", "image"), ("image", pic), ("pages", "2-3"), ("date", "true"))[0];
        using var d = Pig.PdfDocument.Open(o);
        Assert.Equal(new[] { 0, 1, 1 }, d.GetPages().Select(p => p.GetImages().Count()).ToArray());
        var t = PdfKit.Texts(o);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", t[0]);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}", t[1]);
        Assert.Equal(Sizes(src), Sizes(o));
        Assert.StartsWith(PdfKit.Texts(src)[2], t[2]);
    }

    [Fact]
    public void Picture_with_a_caption_name_shows_the_caption()
    {
        var dir = PdfKit.NewDir();
        var pic = PdfTool3Kit.PhotoPng(dir, "sig.png", 200, 80);
        var o = PdfKit.RunOk("pdf-sign-self", new[] { PdfKit.Make(dir, "p.pdf", 1, "PP") }, PdfKit.NewDir("out"),
            ("mode", "image"), ("image", pic), ("name", "A. Lovelace"))[0];
        Assert.Contains("Lovelace", PdfKit.Texts(o)[0]);
    }

    [Fact]
    public void Text_outside_latin1_is_refused_naming_the_option_and_the_character()
    {
        var dir = PdfKit.NewDir();
        var outDir = PdfKit.NewDir("out");
        var r = PdfKit.Run("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 1) }, outDir, ("name", "Zhang \u4E09"));
        Assert.False(r.Ok);
        Assert.Contains("option 'name'", r.Error);
        Assert.Contains("U+4E09", r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Latin1_accents_are_accepted_and_extractable()
    {
        var dir = PdfKit.NewDir();
        var o = PdfKit.RunOk("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 1) }, PdfKit.NewDir("out"), ("name", "Ren\u00E9 M\u00FCller"))[0];
        Assert.Contains("M\u00FCller", PdfKit.Texts(o)[0]);
    }

    [Theory]
    [InlineData("name", "   ", "option 'name'")]
    [InlineData("pages", "9", "option 'pages'")]
    [InlineData("pages", "3-1", "option 'pages'")]
    [InlineData("position", "up-left", "option 'position'")]
    [InlineData("size", "huge", "option 'size'")]
    [InlineData("date", "yes", "option 'date'")]
    public void Bad_options_are_refused_naming_the_option_and_write_nothing(string key, string value, string expected)
    {
        var dir = PdfKit.NewDir();
        var outDir = PdfKit.NewDir("out");
        var opts = new List<(string, string)> { ("name", Name) };
        opts.RemoveAll(o => o.Item1 == key);
        opts.Add((key, value));
        var r = PdfKit.Run("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 2) }, outDir, opts.ToArray());
        Assert.False(r.Ok);
        Assert.Contains(expected, r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Picture_mode_needs_a_png_or_jpg_that_decodes()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "a.pdf", 1);
        var outDir = PdfKit.NewDir("out");
        var none = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("mode", "image"));
        Assert.False(none.Ok);
        Assert.Contains("option 'image'", none.Error);

        var bmp = Path.Combine(dir, "s.bmp");
        File.WriteAllBytes(bmp, new byte[] { 66, 77, 0, 0 });
        var wrongType = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("mode", "image"), ("image", bmp));
        Assert.False(wrongType.Ok);
        Assert.Contains("option 'image'", wrongType.Error);

        var junk = Path.Combine(dir, "s.png");
        File.WriteAllText(junk, "not a png");
        var broken = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("mode", "image"), ("image", junk));
        Assert.False(broken.Ok);
        Assert.Contains("option 'image'", broken.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Input_is_never_overwritten_and_a_second_run_gets_a_new_name()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "a.pdf", 1);
        var bytes = File.ReadAllBytes(src);
        var outDir = dir;   // same folder as the input
        var a = PdfKit.RunOk("pdf-sign-self", new[] { src }, outDir, ("name", Name))[0];
        var b = PdfKit.RunOk("pdf-sign-self", new[] { src }, outDir, ("name", Name))[0];
        Assert.NotEqual(a, b);
        Assert.NotEqual(src, a);
        Assert.Equal(bytes, File.ReadAllBytes(src));
    }

    // ----- certificate step -----

    [Fact]
    public void Certificate_signature_verifies_and_the_visible_mark_and_text_survive()
    {
        var dir = PdfKit.NewDir();
        var src = ThreePages(dir);
        var pfx = PfxFile(dir, "pw-Secret-123");
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"),
            ("name", Name), ("pages", "3"), ("cert", pfx), ("cert_password", "pw-Secret-123"))[0];

        var bytes = File.ReadAllBytes(o);
        Assert.True(PdfCmsSigner.HasDetachedPkcs7Marker(bytes));
        Assert.True(PdfCmsSigner.TryVerify(bytes, out var err), err);
        Assert.Equal(3, PdfKit.PageCount(o));
        Assert.Equal(3, Sizes(o).Count);
        Assert.Equal(Sizes(src).Select(s => (Math.Round(s.W), Math.Round(s.H))), Sizes(o).Select(s => (Math.Round(s.W), Math.Round(s.H))));
        var t = PdfKit.Texts(o);
        Assert.Contains("Lovelace", t[2]);
        Assert.Contains("CONTRACT page 1", t[0]);
        Assert.Contains("CONTRACT page 2", t[1]);
        using (var sharp = PdfReader.Open(o, PdfDocumentOpenMode.Import)) Assert.Equal(3, sharp.PageCount);
    }

    [Fact]
    public void Certificate_signature_fails_verification_when_the_signed_bytes_change()
    {
        var dir = PdfKit.NewDir();
        var pfx = PfxFile(dir, "pw");
        var o = PdfKit.RunOk("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 1, "SIGNED") }, PdfKit.NewDir("out"),
            ("name", Name), ("cert", pfx), ("cert_password", "pw"))[0];
        var bytes = File.ReadAllBytes(o);
        Assert.True(PdfCmsSigner.TryVerify(bytes, out _));
        var s = Encoding.Latin1.GetString(bytes);
        var at = s.IndexOf("%PDF-1.", StringComparison.Ordinal) + 7;
        var tampered = (byte[])bytes.Clone();
        tampered[at] = (byte)(tampered[at] == (byte)'4' ? '5' : '4');
        Assert.False(PdfCmsSigner.TryVerify(tampered, out _));
    }

    [Fact]
    public void Already_signed_file_is_refused_not_silently_invalidated()
    {
        var dir = PdfKit.NewDir();
        var pfx = PfxFile(dir, "pw");
        var first = PdfKit.RunOk("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 2) }, PdfKit.NewDir("o1"),
            ("name", Name), ("cert", pfx), ("cert_password", "pw"))[0];
        var outDir = PdfKit.NewDir("o2");
        var r = PdfKit.Run("pdf-sign-self", new[] { first }, outDir, ("name", Name), ("cert", pfx), ("cert_password", "pw"));
        Assert.False(r.Ok);
        Assert.Contains("already carries a digital signature", r.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Wrong_password_never_echoes_the_password_and_writes_nothing()
    {
        var dir = PdfKit.NewDir();
        var pfx = PfxFile(dir, "right-one");
        var outDir = PdfKit.NewDir("out");
        const string wrong = "Wr0ng-Hunter2-xyzzy";
        var r = PdfKit.Run("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 1) }, outDir,
            ("name", Name), ("cert", pfx), ("cert_password", wrong));
        Assert.False(r.Ok);
        Assert.Contains("option 'cert'", r.Error);
        Assert.DoesNotContain(wrong, r.Error + r.Reason + string.Join("|", r.Outputs));
        Assert.Empty(Directory.GetFileSystemEntries(outDir));

        var (code, so, se) = PdfKit.Cli("pdf-sign-self", new[] { Path.Combine(dir, "a.pdf") }, outDir,
            ("name", Name), ("cert", pfx), ("cert_password", wrong));
        Assert.NotEqual(0, code);
        Assert.DoesNotContain(wrong, so + se);
    }

    [Fact]
    public void Password_is_not_found_in_the_output_file_or_the_output_folder()
    {
        var dir = PdfKit.NewDir();
        const string pw = "T0p-Secret-q1w2e3";
        var pfx = PfxFile(dir, pw);
        var outDir = PdfKit.NewDir("out");
        var (code, so, se) = PdfKit.Cli("pdf-sign-self", new[] { PdfKit.Make(dir, "a.pdf", 1) }, outDir,
            ("name", Name), ("cert", pfx), ("cert_password", pw));
        Assert.Equal(0, code);
        Assert.DoesNotContain(pw, so + se);
        foreach (var f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(pw, Encoding.Latin1.GetString(File.ReadAllBytes(f)));
        Assert.Single(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Password_without_a_certificate_and_a_missing_certificate_are_refused()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "a.pdf", 1);
        var outDir = PdfKit.NewDir("out");
        var a = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("name", Name), ("cert_password", "x"));
        Assert.False(a.Ok);
        Assert.Contains("option 'cert_password'", a.Error);
        var b = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("name", Name), ("cert", Path.Combine(dir, "nope.pfx")));
        Assert.False(b.Ok);
        Assert.Contains("option 'cert'", b.Error);
        var notPfx = Path.Combine(dir, "x.pfx");
        File.WriteAllText(notPfx, "plain text");
        var c = PdfKit.Run("pdf-sign-self", new[] { src }, outDir, ("name", Name), ("cert", notPfx));
        Assert.False(c.Ok);
        Assert.Contains("option 'cert'", c.Error);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Cancelled_run_leaves_nothing_behind()
    {
        var dir = PdfKit.NewDir();
        var outDir = PdfKit.NewDir("out");
        var tool = PdfKit.Registry.Find("pdf-sign-self")!;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = NetieSpace.Apps.Tools.ToolRunner.RunAsync(tool,
            new NetieSpace.Apps.Tools.ToolRequest(new[] { PdfKit.Make(dir, "a.pdf", 1) }, outDir,
                new Dictionary<string, string> { ["name"] = Name }), null, cts.Token).GetAwaiter().GetResult();
        Assert.False(r.Ok);
        Assert.Empty(Directory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public void Rotated_page_gets_the_mark_upright_in_the_displayed_corner()
    {
        var dir = PdfKit.NewDir();
        var src = PdfKit.Make(dir, "r.pdf", 1, "RR", tweak: (p, _) => p.Rotate = 90);
        var o = PdfKit.RunOk("pdf-sign-self", new[] { src }, PdfKit.NewDir("out"), ("name", Name), ("position", "bottom-right"))[0];
        Assert.Contains("Lovelace", PdfKit.Texts(o)[0]);
        Assert.Equal(new[] { 90 }, PdfKit.Rotations(o));
    }
}
