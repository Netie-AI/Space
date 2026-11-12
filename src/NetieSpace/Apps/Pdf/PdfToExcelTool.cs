using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NetieSpace.Apps.Tools;
using UglyToad.PdfPig.Content;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// pdf-to-excel: only where a page really holds a grid. Words are grouped into lines; a table is a run of at
/// least three consecutive lines that each split into two or more cells by wide horizontal gaps, whose cells
/// fall into the same vertical white channels (columns). One sheet per page that has a table. A PDF with no
/// such structure is refused rather than dumped as loose words. Cells with a long wrapped text, tables whose
/// columns touch, merged cells and ruled-only grids (no gap between columns) are not recognised.
/// </summary>
internal static class PdfToExcelTool
{
    private const int MinRows = 3;
    private const int MaxWordsPerCell = 5;

    public static Task<IReadOnlyList<string>> RunAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var numbers = OptBool(req, "numbers", true);
        return PdfTool3Batch.Run(req, progress, ct, "", ".xlsx", (input, output, token) => Convert(input, output, numbers, token));
    }

    private sealed record GCell(double Left, double Right, string Text);

    private sealed class GRow
    {
        public PdfTextLine Line = null!;
        public List<GCell> Cells = new();
        public bool Multi => Cells.Count >= 2;
    }

    private static void Convert(string input, string output, bool numbers, CancellationToken ct)
    {
        UglyToad.PdfPig.PdfDocument doc;
        try { doc = UglyToad.PdfPig.PdfDocument.Open(input); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"cannot read as a PDF: {ex.Message} (password protected files need pdf-unlock first; damaged files need pdf-repair)", ex);
        }

        var sheets = new List<(int Page, List<List<string[]>> Tables)>();
        using (doc)
        {
            for (var n = 1; n <= doc.NumberOfPages; n++)
            {
                ct.ThrowIfCancellationRequested();
                var tables = FindTables(PdfText.Lines(doc.GetPage(n).GetWords()));
                if (tables.Count > 0) sheets.Add((n, tables));
            }
        }
        if (sheets.Count == 0)
            throw new InvalidOperationException(
                $"no table structure found: a table needs at least {MinRows} rows with 2 or more columns separated by clear gaps (use pdf-extract-text for running text, or pdf-ocr first for a scan)");
        Write(output, sheets, numbers, ct);
    }

    // ----- table detection -----

    private static List<GRow> ToRows(List<PdfTextLine> lines)
    {
        var rows = new List<GRow>();
        foreach (var l in lines)
        {
            var gapLimit = 1.2 * l.Size;
            var row = new GRow { Line = l };
            var words = new List<Word>();
            void Close()
            {
                if (words.Count == 0) return;
                var text = string.Join(" ", words.Select(w => PdfText.Clean(w.Text)).Where(t => t.Length > 0));
                if (text.Length > 0) row.Cells.Add(new GCell(words.Min(w => w.BoundingBox.Left), words.Max(w => w.BoundingBox.Right), text));
                words.Clear();
            }
            Word? prev = null;
            foreach (var w in l.Words)
            {
                if (prev is not null && w.BoundingBox.Left - prev.BoundingBox.Right > gapLimit) Close();
                words.Add(w);
                prev = w;
            }
            Close();
            if (row.Cells.Count > 0) rows.Add(row);
        }
        return rows;
    }

    private static bool Close(GRow a, GRow b) =>
        a.Line.Baseline - b.Line.Baseline <= 2.6 * Math.Max(a.Line.Size, b.Line.Size);

    private static List<List<string[]>> FindTables(List<PdfTextLine> lines)
    {
        var rows = ToRows(lines);
        var tables = new List<List<string[]>>();

        // Candidate runs: multi-cell rows, plus single-cell rows that sit between two multi-cell rows.
        var work = new Stack<List<GRow>>();
        var runs = new List<List<GRow>>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Multi) continue;
            var run = new List<GRow> { rows[i] };
            var k = i;
            while (k + 1 < rows.Count && Close(rows[k], rows[k + 1]))
            {
                if (rows[k + 1].Multi) { k++; run.Add(rows[k]); continue; }
                if (k + 2 < rows.Count && rows[k + 2].Multi && Close(rows[k + 1], rows[k + 2]))
                {
                    run.Add(rows[k + 1]); run.Add(rows[k + 2]); k += 2; continue;
                }
                break;
            }
            runs.Add(run);
            i = k;
        }
        foreach (var r in runs) work.Push(r);

        var accepted = new List<(double Top, List<string[]> Grid)>();
        while (work.Count > 0)
        {
            var run = work.Pop();
            var multi = run.Where(r => r.Multi).ToList();
            if (multi.Count < MinRows) continue;
            var size = multi.Select(r => r.Line.Size).OrderBy(x => x).ElementAt(multi.Count / 2);
            var bands = Bands(multi, 1.2 * size);
            if (bands.Count < 2) continue;

            // A single-cell row that straddles a column gutter is running text, not a row: cut the run there.
            var cutAt = run.FindIndex(r => !r.Multi && BandsHit(r.Cells[0], bands).Count > 1);
            if (cutAt >= 0)
            {
                work.Push(run.Take(cutAt).ToList());
                work.Push(run.Skip(cutAt + 1).ToList());
                continue;
            }

            var cellWords = multi.SelectMany(r => r.Cells).Select(c => c.Text.Split(' ').Length).OrderBy(x => x).ToList();
            if (cellWords[cellWords.Count / 2] > MaxWordsPerCell) continue; // columns of running text, not a table

            var grid = new List<string[]>();
            foreach (var r in run)
            {
                var cells = new string[bands.Count];
                foreach (var c in r.Cells)
                {
                    var b = BandOf(c, bands);
                    cells[b] = cells[b] is null ? c.Text : cells[b] + " " + c.Text;
                }
                grid.Add(cells.Select(x => x ?? "").ToArray());
            }
            // Every column must carry text in at least two rows, else it is a stray label.
            var used = Enumerable.Range(0, bands.Count).Where(c => grid.Count(g => g[c].Length > 0) >= 2).ToList();
            if (used.Count < 2) continue;
            grid = grid.Select(g => used.Select(c => g[c]).ToArray()).Where(g => g.Any(x => x.Length > 0)).ToList();
            if (grid.Count < MinRows) continue;
            accepted.Add((run[0].Line.Top, grid));
        }
        foreach (var t in accepted.OrderByDescending(a => a.Top)) tables.Add(t.Grid);
        return tables;
    }

    private static List<(double L, double R)> Bands(List<GRow> multi, double gap)
    {
        var spans = multi.SelectMany(r => r.Cells).Select(c => (c.Left, c.Right)).OrderBy(s => s.Left).ToList();
        var bands = new List<(double L, double R)>();
        foreach (var s in spans)
        {
            if (bands.Count > 0 && s.Left - bands[^1].R < gap) bands[^1] = (bands[^1].L, Math.Max(bands[^1].R, s.Right));
            else bands.Add((s.Left, s.Right));
        }
        return bands;
    }

    private static List<int> BandsHit(GCell c, List<(double L, double R)> bands) =>
        Enumerable.Range(0, bands.Count).Where(i => c.Right > bands[i].L && c.Left < bands[i].R).ToList();

    private static int BandOf(GCell c, List<(double L, double R)> bands)
    {
        var hit = BandsHit(c, bands);
        if (hit.Count > 0) return hit[0];
        var mid = (c.Left + c.Right) / 2;
        return Enumerable.Range(0, bands.Count).OrderBy(i => Math.Abs((bands[i].L + bands[i].R) / 2 - mid)).First();
    }

    // ----- workbook -----

    private static readonly Regex Plain = new(@"^-?(0|[1-9][0-9]{0,14})(\.[0-9]*[1-9])?$", RegexOptions.Compiled);

    private static bool IsExactNumber(string t)
    {
        if (!Plain.IsMatch(t)) return false;
        if (t.Count(char.IsDigit) > 15) return false;
        return double.Parse(t, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) == t;
    }

    private static string ColName(int zeroBased)
    {
        var s = "";
        for (var n = zeroBased + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static void Write(string path, List<(int Page, List<List<string[]>> Tables)> sheets, bool numbers, CancellationToken ct)
    {
        using var pkg = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = pkg.AddWorkbookPart();
        wb.Workbook = new Workbook();
        var sheetList = wb.Workbook.AppendChild(new Sheets());
        uint sheetId = 1;
        foreach (var (page, tables) in sheets)
        {
            ct.ThrowIfCancellationRequested();
            var ws = wb.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            uint r = 1;
            var widths = new List<double>();
            for (var t = 0; t < tables.Count; t++)
            {
                if (t > 0) r++;                       // one empty row between two tables on the same page
                foreach (var grid in tables[t])
                {
                    var row = new Row { RowIndex = r };
                    for (var c = 0; c < grid.Length; c++)
                    {
                        var text = grid[c];
                        while (widths.Count <= c) widths.Add(8);
                        widths[c] = Math.Max(widths[c], Math.Min(60, text.Length + 2));
                        if (text.Length == 0) continue;
                        var cell = new Cell { CellReference = ColName(c) + r };
                        if (numbers && IsExactNumber(text))
                        {
                            cell.DataType = CellValues.Number;
                            cell.CellValue = new CellValue(text);
                        }
                        else
                        {
                            cell.DataType = CellValues.InlineString;
                            cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                        }
                        row.Append(cell);
                    }
                    data.Append(row);
                    r++;
                }
            }
            var columns = new Columns(widths.Select((w, i) => new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = w, CustomWidth = true }));
            ws.Worksheet = new Worksheet(columns, data);
            sheetList.Append(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = sheetId++, Name = "Page " + page });
        }
        wb.Workbook.Save();
    }
}
