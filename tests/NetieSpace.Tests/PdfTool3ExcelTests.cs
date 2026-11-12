using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace NetieSpace.Tests;

public class PdfTool3ExcelTests
{
    private static readonly string[][] Prices =
    {
        new[] { "Item", "Qty", "Price" },
        new[] { "Widget", "12", "3.50" },
        new[] { "Gadget", "7", "10.25" },
        new[] { "Bolt", "150", "0.05" },
        new[] { "Nut", "007", "1,234" },
    };

    private static readonly string[][] Second =
    {
        new[] { "Region", "Units", "Share" },
        new[] { "North", "40", "50%" },
        new[] { "South", "25", "31%" },
        new[] { "West", "15", "19%" },
    };

    private static readonly string[][] Third =
    {
        new[] { "Code", "Stock", "Bin" },
        new[] { "A-1", "9", "x1" },
        new[] { "B-2", "11", "y4" },
    };

    private sealed record Cellv(string Ref, string Type, string Text);

    private static List<(string Name, List<List<Cellv>> Rows)> ReadBook(string xlsx)
    {
        using var d = SpreadsheetDocument.Open(xlsx, false);
        var wb = d.WorkbookPart!;
        var result = new List<(string, List<List<Cellv>>)>();
        foreach (var sheet in wb.Workbook.Sheets!.Elements<Sheet>())
        {
            var ws = (WorksheetPart)wb.GetPartById(sheet.Id!);
            var rows = new List<List<Cellv>>();
            foreach (var row in ws.Worksheet.Descendants<Row>())
            {
                var cells = new List<Cellv>();
                foreach (var c in row.Elements<Cell>())
                {
                    var type = c.DataType?.Value == CellValues.InlineString ? "text" : "number";
                    var text = c.DataType?.Value == CellValues.InlineString ? c.InlineString!.Text!.Text : c.CellValue!.Text;
                    cells.Add(new Cellv(c.CellReference!.Value!, type, text));
                }
                rows.Add(cells);
            }
            result.Add((sheet.Name!.Value!, rows));
        }
        return result;
    }

    private static string[][] Grid(List<List<Cellv>> rows, int cols) =>
        rows.Select(r => Enumerable.Range(0, cols).Select(c => r.FirstOrDefault(x => x.Ref.StartsWith(((char)('A' + c)).ToString()) && char.IsDigit(x.Ref[1]))?.Text ?? "").ToArray()).ToArray();

    [Fact]
    public void A_table_page_becomes_a_sheet_and_every_cell_reads_back_exactly()
    {
        var pdf = PdfTool3Kit.Tables(PdfKit.NewDir(), "prices.pdf", Prices, Second, Third);

        var xlsx = Assert.Single(PdfKit.RunOk("pdf-to-excel", new[] { pdf }, PdfKit.NewDir(), ("numbers", "false")));

        Assert.EndsWith(".xlsx", xlsx);
        var book = ReadBook(xlsx);
        Assert.Equal(new[] { "Page 1", "Page 3" }, book.Select(b => b.Name).ToArray());   // the prose page has no sheet
        Assert.Equal(Prices, Grid(book[0].Rows, 3));
        // Page 3 holds two tables: stacked on one sheet with one empty row between them.
        var p3 = book[1].Rows;
        Assert.Equal(Second, Grid(p3.Take(4).ToList(), 3));
        Assert.Equal(Third, Grid(p3.Skip(4).ToList(), 3));
        Assert.Equal("A6", p3[4][0].Ref);            // rows 1-4, row 5 left empty, the next table starts at row 6
        Assert.Equal(new[] { "A5", "B5", "C5" }, book[0].Rows[4].Select(c => c.Ref).ToArray());
    }

    [Fact]
    public void Plain_numbers_are_numbers_and_anything_that_would_change_stays_text()
    {
        var pdf = PdfTool3Kit.Tables(PdfKit.NewDir(), "prices.pdf", Prices, Second, Third);

        var xlsx = Assert.Single(PdfKit.RunOk("pdf-to-excel", new[] { pdf }, PdfKit.NewDir()));

        var rows = ReadBook(xlsx)[0].Rows;
        Cellv At(int row, string col) => rows[row].Single(c => c.Ref == col + (row + 1));
        Assert.Equal("number", At(1, "B").Type);
        Assert.Equal("12", At(1, "B").Text);
        Assert.Equal("text", At(1, "C").Type);      // "3.50" would print as 3.5 in a number cell
        Assert.Equal("3.50", At(1, "C").Text);
        Assert.Equal("number", At(3, "C").Type);    // 0.05 survives a double round trip unchanged
        Assert.Equal("0.05", At(3, "C").Text);
        Assert.Equal("text", At(4, "B").Type);      // leading zeros are kept
        Assert.Equal("007", At(4, "B").Text);
        Assert.Equal("1,234", At(4, "C").Text);
        Assert.Equal("text", At(0, "A").Type);
    }

    [Fact]
    public void A_pdf_of_running_text_is_refused_not_dumped_as_words()
    {
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.Prose(dir, "prose.pdf");
        var outDir = PdfKit.NewDir();

        var r = PdfKit.Run("pdf-to-excel", new[] { pdf }, outDir);

        Assert.False(r.Ok);
        Assert.Contains("no table structure found", r.Error);
        Assert.Contains("prose.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public void Two_columns_of_prose_are_not_mistaken_for_a_table()
    {
        var pdf = PdfTool3Kit.TwoColumnProse(PdfKit.NewDir(), "news.pdf");
        var r = PdfKit.Run("pdf-to-excel", new[] { pdf }, PdfKit.NewDir());
        Assert.False(r.Ok);
        Assert.Contains("no table structure found", r.Error);
    }

    [Fact]
    public void A_row_with_an_empty_cell_and_a_row_with_one_cell_stay_inside_the_table()
    {
        var rows = new[]
        {
            new[] { "Name", "Dept", "Level" },
            new[] { "Ann", "", "3" },
            new[] { "Notes", "", "" },
            new[] { "Bo", "Ops", "" },
            new[] { "Cy", "IT", "2" },
        };
        var dir = PdfKit.NewDir();
        var pdf = PdfTool3Kit.Tables(dir, "sparse.pdf", rows, Second, Third);

        var xlsx = Assert.Single(PdfKit.RunOk("pdf-to-excel", new[] { pdf }, PdfKit.NewDir(), ("numbers", "false")));

        Assert.Equal(rows, Grid(ReadBook(xlsx)[0].Rows, 3));
    }

    [Fact]
    public void Same_named_inputs_do_not_collide_and_the_input_is_untouched()
    {
        var a = PdfKit.NewDir();
        var b = PdfKit.NewDir();
        var pa = PdfTool3Kit.Tables(a, "t.pdf", Prices, Second, Third);
        var pb = PdfTool3Kit.Tables(b, "t.pdf", Prices, Second, Third);
        var before = IoFile.ReadAllBytes(pa);

        var outs = PdfKit.RunOk("pdf-to-excel", new[] { pa, pb }, PdfKit.NewDir());

        Assert.Equal(2, outs.Count);
        Assert.NotEqual(outs[0], outs[1]);
        Assert.Equal(before, IoFile.ReadAllBytes(pa));
    }

    [Fact]
    public void One_prose_file_in_a_batch_leaves_no_partial_output()
    {
        var dir = PdfKit.NewDir();
        var good = PdfTool3Kit.Tables(dir, "good.pdf", Prices, Second, Third);
        var bad = PdfTool3Kit.Prose(dir, "bad.pdf");
        var outDir = PdfKit.NewDir();

        var r = PdfKit.Run("pdf-to-excel", new[] { good, bad }, outDir);

        Assert.False(r.Ok);
        Assert.Contains("bad.pdf", r.Error);
        Assert.Empty(Directory.GetFiles(outDir));
    }

    [Fact]
    public void A_bad_numbers_option_names_the_option()
    {
        var pdf = PdfTool3Kit.Tables(PdfKit.NewDir(), "t.pdf", Prices, Second, Third);
        var r = PdfKit.Run("pdf-to-excel", new[] { pdf }, PdfKit.NewDir(), ("numbers", "sometimes"));
        Assert.False(r.Ok);
        Assert.Contains("numbers", r.Error);
    }
}
