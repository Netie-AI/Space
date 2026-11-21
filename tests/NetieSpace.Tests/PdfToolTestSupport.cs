using System.Text;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Pig = UglyToad.PdfPig;
using Sharp = PdfSharpCore.Pdf;

namespace NetieSpace.Tests;

/// <summary>Builds real PDFs with PdfSharpCore and reads results back with PdfPig, so tests judge the artifact.</summary>
internal static class PdfKit
{
    public static readonly ToolRegistry Registry = ToolRegistry.Load(probe: new EngineProbes());

    public static string NewDir(string? sub = null)
    {
        var d = ToolTestSupport.NewTempDir();
        if (sub is null) return d;
        var s = IoPath.Combine(d, sub);
        IoDirectory.CreateDirectory(s);
        return s;
    }

    /// <summary>A PDF whose page i carries the text "{tag} page {i}" (1-based).</summary>
    public static string Make(string dir, string name, int pages, string tag = "DOC", double width = 612, double height = 792, Action<PdfPage, int>? tweak = null, int extraLines = 0)
    {
        using var doc = new Sharp.PdfDocument();
        for (var i = 1; i <= pages; i++)
        {
            var page = doc.AddPage();
            page.Width = width; page.Height = height;
            using (var g = XGraphics.FromPdfPage(page))
            {
                var f = new XFont("Arial", 14);
                g.DrawString($"{tag} page {i}", f, XBrushes.Black, new XPoint(60, 100));
                var rnd = new Random(i * 7919 + tag.Length);
                for (var l = 0; l < extraLines; l++)
                    g.DrawString(new string(Enumerable.Range(0, 60).Select(_ => (char)('a' + rnd.Next(26))).ToArray()), f, XBrushes.Black, new XPoint(60, 130 + l * 12));
            }
            tweak?.Invoke(page, i);
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    public static int PageCount(string path, string? password = null)
    {
        using var d = Open(path, password);
        return d.NumberOfPages;
    }

    public static Pig.PdfDocument Open(string path, string? password = null) =>
        password is null
            ? Pig.PdfDocument.Open(path)
            : Pig.PdfDocument.Open(path, new Pig.ParsingOptions { Passwords = new List<string> { password }, UseLenientParsing = false });

    public static List<string> Texts(string path, string? password = null)
    {
        using var d = Open(path, password);
        return d.GetPages().Select(p => p.Text).ToList();
    }

    public static ToolResult Run(string id, IEnumerable<string> files, string outDir, params (string Key, string Value)[] opts)
    {
        var tool = Registry.Find(id) ?? throw new InvalidOperationException("no such tool " + id);
        var req = new ToolRequest(files.ToList(), outDir, opts.ToDictionary(o => o.Key, o => o.Value));
        return ToolRunner.RunAsync(tool, req, null, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static IReadOnlyList<string> RunOk(string id, IEnumerable<string> files, string outDir, params (string Key, string Value)[] opts)
    {
        var r = Run(id, files, outDir, opts);
        Assert.True(r.Ok, id + " failed: " + r.Error);
        return r.Outputs;
    }

    /// <summary>The --tool path: registry, availability, validation and runner together.</summary>
    public static (int Exit, string Out, string Err) Cli(string id, IEnumerable<string> files, string outDir, params (string Key, string Value)[] opts)
    {
        var args = new List<string> { "--tool", id, "--out", outDir };
        foreach (var f in files) { args.Add("--in"); args.Add(f); }
        foreach (var o in opts) { args.Add("--opt"); args.Add(o.Key + "=" + o.Value); }
        var so = new StringWriter();
        var se = new StringWriter();
        var code = ToolCli.RunWith(args.ToArray(), so, se, Registry);
        return (code, so.ToString(), se.ToString());
    }

    public static string[] Lines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    public static List<int> Rotations(string path)
    {
        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, d.PageCount).Select(i => d.Pages[i].Rotate).ToList();
    }

    /// <summary>Crop box as written in the file: x1, y1, x2, y2 in PDF user space (y up), per page.</summary>
    public static List<double[]> CropBoxes(string path)
    {
        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, d.PageCount).Select(i =>
        {
            var b = d.Pages[i].CropBox;
            return new[] { b.X1, b.Y1, b.X2, b.Y2 };
        }).ToList();
    }

    public static byte[] Bytes(string path) => IoFile.ReadAllBytes(path);

    public static string Latin1(string path) => Encoding.Latin1.GetString(IoFile.ReadAllBytes(path));
}
