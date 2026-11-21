using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sharp = PdfSharpCore.Pdf;

namespace NetieSpace.Tests;

/// <summary>Fixtures for the third wave of PDF tools: real PDFs, real pictures, built here and judged by PdfPig.</summary>
internal static class PdfTool3Kit
{
    /// <summary>Photograph-like picture: smooth gradient with deterministic noise, so lossless coding is large.</summary>
    public static string PhotoPng(string dir, string name, int w, int h, int seed = 1, bool alpha = false)
    {
        var path = IoPath.Combine(dir, name);
        var rnd = new Random(seed);
        if (alpha)
        {
            using var img = new Image<Rgba32>(w, h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var n = rnd.Next(-18, 19);
                    img[x, y] = new Rgba32(Clamp(x * 255 / w + n), Clamp(y * 255 / h + n), Clamp(128 + n), (byte)(x < w / 2 ? 255 : 90));
                }
            img.SaveAsPng(path);
        }
        else
        {
            using var img = new Image<Rgb24>(w, h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var n = rnd.Next(-18, 19);
                    img[x, y] = new Rgb24(Clamp(x * 255 / w + n), Clamp(y * 255 / h + n), Clamp(128 + (x + y) % 40 + n));
                }
            img.SaveAsPng(path);
        }
        return path;
    }

    public static string PhotoJpeg(string dir, string name, int w, int h, int seed = 1)
    {
        var png = PhotoPng(dir, "src-" + name + ".png", w, h, seed);
        var path = IoPath.Combine(dir, name);
        using var img = Image.Load(png);
        img.SaveAsJpeg(path, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 98 });
        return path;
    }

    private static byte Clamp(int v) => (byte)Math.Max(0, Math.Min(255, v));

    /// <summary>One page: a text line, then each picture drawn at the given size in points (stacked down the page).</summary>
    public static string PdfWithImages(string dir, string name, string text, params (string Image, double W, double H)[] pictures)
    {
        using var doc = new Sharp.PdfDocument();
        doc.Options.CompressContentStreams = true;
        var page = doc.AddPage();
        page.Width = 612; page.Height = 792;
        using (var g = XGraphics.FromPdfPage(page))
        {
            g.DrawString(text, new XFont("Arial", 14), XBrushes.Black, new XPoint(40, 40));
            var y = 60.0;
            foreach (var (image, w, h) in pictures)
            {
                using var xi = XImage.FromFile(image);
                g.DrawImage(xi, 40, y, w, h);
                y += h + 10;
            }
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>Text only, streams already compressed: nothing left for a compressor to win.</summary>
    public static string TextOnly(string dir, string name, int pages = 2)
    {
        using var doc = new Sharp.PdfDocument();
        doc.Options.CompressContentStreams = true;
        for (var i = 1; i <= pages; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            g.DrawString("Plain page " + i, new XFont("Arial", 14), XBrushes.Black, new XPoint(60, 100));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>Many lines of text with the content streams left uncompressed.</summary>
    public static string TextHeavyUncompressed(string dir, string name, int pages, int lines)
    {
        using var doc = new Sharp.PdfDocument();
        doc.Options.CompressContentStreams = false;
        for (var p = 1; p <= pages; p++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            var f = new XFont("Arial", 9);
            for (var l = 0; l < lines; l++)
                g.DrawString($"Row {p}.{l} the quick brown fox jumps over the lazy dog", f, XBrushes.Black, new XPoint(40, 30 + l * 11));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>Image XObjects as written in the file: (object number, dictionary, raw stream bytes).</summary>
    public static List<(int Id, PdfDictionary Dict, byte[] Raw)> ImageObjects(string path)
    {
        using var d = PdfSharpCore.Pdf.IO.PdfReader.Open(path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);
        var list = new List<(int, PdfDictionary, byte[])>();
        foreach (var o in d.Internals.GetAllObjects())
            if (o is PdfDictionary dict && dict.Elements.ContainsKey("/Subtype") && dict.Elements["/Subtype"] is PdfName { Value: "/Image" } && dict.Stream is not null)
                list.Add((o.Reference.ObjectID.ObjectNumber, dict, dict.Stream.Value));
        return list;
    }

    /// <summary>
    /// Two pages. Page 1: a 24 pt heading, a three-line paragraph, a wider gap, a two-line paragraph.
    /// Page 2: a 15 pt heading, a one-line paragraph and, if given, a picture drawn between them.
    /// </summary>
    public static string Prose(string dir, string name, string? picture = null, double pageW = 612, double pageH = 792)
    {
        using var doc = new Sharp.PdfDocument();
        doc.Options.CompressContentStreams = true;
        var body = new XFont("Arial", 11);
        var p1 = doc.AddPage(); p1.Width = pageW; p1.Height = pageH;
        using (var g = XGraphics.FromPdfPage(p1))
        {
            g.DrawString("Annual Report", new XFont("Arial", 24), XBrushes.Black, new XPoint(60, 80));
            var y = 120.0;
            foreach (var l in new[] { "The first paragraph starts here and", "continues on a second line before", "ending on the third line." })
            { g.DrawString(l, body, XBrushes.Black, new XPoint(60, y)); y += 14; }
            y += 22;
            foreach (var l in new[] { "A second paragraph follows after a gap", "and has two lines with caf\u00e9 and 100% growth." })
            { g.DrawString(l, body, XBrushes.Black, new XPoint(60, y)); y += 14; }
        }
        var p2 = doc.AddPage(); p2.Width = pageW; p2.Height = pageH;
        using (var g = XGraphics.FromPdfPage(p2))
        {
            g.DrawString("Second Page", new XFont("Arial", 15), XBrushes.Black, new XPoint(60, 80));
            g.DrawString("Intro line before the picture.", body, XBrushes.Black, new XPoint(60, 110));
            if (picture is not null)
            {
                using var xi = XImage.FromFile(picture);
                g.DrawImage(xi, 60, 130, 150, 100);
            }
            g.DrawString("Closing line after the picture.", body, XBrushes.Black, new XPoint(60, picture is null ? 140 : 260));
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>A PDF whose only content is a picture: no text layer at all.</summary>
    public static string ImageOnly(string dir, string name, string picture)
    {
        using var doc = new Sharp.PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page))
        {
            using var xi = XImage.FromFile(picture);
            g.DrawImage(xi, 40, 40, 300, 200);
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>Draws rows of cells at fixed column x positions starting at y; returns the y after the last row.</summary>
    public static double DrawGrid(XGraphics g, double y, double[] xs, string[][] rows, double lead = 16)
    {
        var f = new XFont("Arial", 11);
        foreach (var r in rows)
        {
            for (var c = 0; c < r.Length; c++)
                if (r[c].Length > 0) g.DrawString(r[c], f, XBrushes.Black, new XPoint(xs[c], y));
            y += lead;
        }
        return y;
    }

    /// <summary>Page 1: one table. Page 2: running text only. Page 3: two tables with space between them.</summary>
    public static string Tables(string dir, string name, string[][] first, string[][] second, string[][] third)
    {
        using var doc = new Sharp.PdfDocument();
        doc.Options.CompressContentStreams = true;
        var xs = new[] { 60.0, 230.0, 380.0 };
        var p1 = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(p1))
        {
            g.DrawString("Price list", new XFont("Arial", 11), XBrushes.Black, new XPoint(60, 60));
            DrawGrid(g, 100, xs, first);
        }
        var p2 = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(p2))
        {
            var f = new XFont("Arial", 11);
            var y = 100.0;
            foreach (var l in new[] { "This page is only running text without any grid at all.", "It has several lines that read from the left margin to the right.", "A reader would never call any of this a table of values." })
            { g.DrawString(l, f, XBrushes.Black, new XPoint(60, y)); y += 16; }
        }
        var p3 = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(p3))
        {
            var y = DrawGrid(g, 100, xs, second);
            DrawGrid(g, y + 90, xs, third);
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }

    /// <summary>Two columns of running prose, the way a newspaper sets it.</summary>
    public static string TwoColumnProse(string dir, string name)
    {
        using var doc = new Sharp.PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page))
        {
            var f = new XFont("Arial", 10);
            for (var i = 0; i < 12; i++)
            {
                g.DrawString($"Left column line {i} has some plain words", f, XBrushes.Black, new XPoint(50, 80 + i * 14));
                g.DrawString($"Right column line {i} has more plain words", f, XBrushes.Black, new XPoint(320, 80 + i * 14));
            }
        }
        var path = IoPath.Combine(dir, name);
        doc.Save(path);
        return path;
    }
}
