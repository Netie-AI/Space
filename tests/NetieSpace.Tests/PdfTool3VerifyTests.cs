using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Tests;

public class PdfTool3VerifyTests
{
    [Fact]
    public void A_rotated_page_gets_the_displayed_page_size_in_word()
    {
        var dir = PdfKit.NewDir();
        var path = IoPath.Combine(dir, "rot.pdf");
        using (var doc = new PdfDocument())
        {
            var p = doc.AddPage(); p.Width = 612; p.Height = 792;
            using (var g = XGraphics.FromPdfPage(p)) g.DrawString("Rotated page text", new XFont("Arial", 12), XBrushes.Black, new XPoint(60, 100));
            p.Rotate = 90;
            doc.Save(path);
        }
        var docx = Assert.Single(PdfKit.RunOk("pdf-to-word", new[] { path }, PdfKit.NewDir()));
        using var w = WordprocessingDocument.Open(docx, false);
        var sp = w.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.PageSize>().Single();
        using var d = UglyToad.PdfPig.PdfDocument.Open(path);
        Assert.Equal((uint)Math.Round(d.GetPage(1).Width * 20), sp.Width!.Value);
        Assert.Equal((uint)Math.Round(d.GetPage(1).Height * 20), sp.Height!.Value);
        Assert.True(sp.Width!.Value > sp.Height!.Value);
    }

    [Fact]
    public void Negative_zero_stays_text_in_excel()
    {
        var dir = PdfKit.NewDir();
        var path = IoPath.Combine(dir, "t.pdf");
        using (var doc = new PdfDocument())
        {
            var p = doc.AddPage();
            using var g = XGraphics.FromPdfPage(p);
            PdfTool3Kit.DrawGrid(g, 100, new[] { 60.0, 230.0, 380.0 },
                new[] { new[] { "a", "b", "c" }, new[] { "-0", "1", "2" }, new[] { "x", "-12.5", "3" } });
            doc.Save(path);
        }
        var xlsx = Assert.Single(PdfKit.RunOk("pdf-to-excel", new[] { path }, PdfKit.NewDir()));
        using var s = SpreadsheetDocument.Open(xlsx, false);
        var cells = s.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("-0", cells["A2"].InlineString!.Text!.Text);
        Assert.Equal(CellValues.Number, cells["B3"].DataType!.Value);
        Assert.Equal("-12.5", cells["B3"].CellValue!.Text);
    }

    [Fact]
    public void Pictures_that_differ_in_a_display_entry_are_not_merged()
    {
        var dir = PdfKit.NewDir();
        var photo = PdfTool3Kit.PhotoPng(dir, "same.png", 600, 450);
        using var doc = new PdfDocument();
        for (var i = 0; i < 3; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            using var xi = XImage.FromFile(photo);
            g.DrawImage(xi, 40, 60, 600, 450);
        }
        var pdf = IoPath.Combine(dir, "three.pdf");
        doc.Save(pdf);
        // Mark the second copy as not smoothed (the writer smooths by default): same pixels, different display.
        string marked;
        using (var d = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify))
        {
            var imgs = d.Internals.GetAllObjects().OfType<PdfDictionary>()
                .Where(x => x.Elements.ContainsKey("/Subtype") && x.Elements["/Subtype"] is PdfName { Value: "/Image" }).ToList();
            Assert.Equal(3, imgs.Count);
            imgs[1].Elements["/Interpolate"] = new PdfBoolean(false);
            marked = IoPath.Combine(dir, "marked.pdf");
            d.Save(marked);
        }
        var outPath = Assert.Single(PdfKit.RunOk("pdf-compress", new[] { marked }, PdfKit.NewDir(), ("level", "low")));
        var kept = PdfTool3Kit.ImageObjects(outPath);
        Assert.Equal(2, kept.Count);
        Assert.Single(kept, k => k.Dict.Elements["/Interpolate"] is PdfBoolean { Value: false });
    }
}
