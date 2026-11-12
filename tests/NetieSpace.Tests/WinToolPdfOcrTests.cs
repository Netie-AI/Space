// pdf-ocr runs on Windows.Media.Ocr, so these tests exist only in the Windows shape.
// A machine with no OCR language pack must FAIL them, with a message that says so.
#if WINDOWS
using NetieSpace.Apps.Pdf.WinRt;
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

public class WinToolPdfOcrTests
{
    private const string NoLanguage =
        "no OCR language pack is installed on this machine, so pdf-ocr cannot run; add an OCR language in Windows language settings";

    [Fact]
    public void An_ocr_language_is_installed_on_this_machine()
    {
        var installed = PdfOcrToolProvider.InstalledLanguages();
        Assert.True(installed.Count > 0, NoLanguage);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        Assert.True(engine is not null,
            "no OCR language matches the Windows languages of this user (installed OCR languages: "
            + string.Join(", ", installed) + ")");
    }

    [Fact]
    public void Pdf_ocr_is_registered_and_available_on_windows()
    {
        var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find("pdf-ocr");
        Assert.NotNull(tool);
        Assert.NotNull(tool!.Runner);
        Assert.True(tool.Availability.Available, tool.Availability.Text);
    }

    [Fact]
    public void Ocr_text_of_page_one_contains_the_long_words_the_pdf_text_layer_has()
    {
        Assert.True(PdfOcrToolProvider.InstalledLanguages().Count > 0, NoLanguage);

        var input = WinToolSupport.Fixture("Q3_Report.pdf");
        int pages;
        List<string> words;
        using (var doc = PdfDocument.Open(input))
        {
            pages = doc.NumberOfPages;
            // Long all-letter words are the ones OCR gets whole; the longest distinct ones are the most telling.
            words = doc.GetPage(1).GetWords()
                .Select(w => w.Text.Trim())
                .Where(t => t.Length >= 7 && t.All(char.IsLetter))
                .GroupBy(t => t.ToLowerInvariant())
                .Select(g => g.First())
                .OrderByDescending(t => t.Length)
                .Take(8)
                .ToList();
        }
        Assert.True(words.Count >= 4, "the fixture's first page has only " + words.Count + " long words to compare");

        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "not yet there");
        var (code, stdout, stderr) = WinToolSupport.Cli("--tool", "pdf-ocr", "--in", input, "--out", outDir);
        Assert.True(code == 0, stderr);

        var txt = IoPath.Combine(outDir, "Q3_Report.txt");
        Assert.True(IoFile.Exists(txt), "expected " + txt);
        Assert.Contains(txt, stdout);
        var text = IoFile.ReadAllText(txt);
        for (var p = 1; p <= pages; p++)
            Assert.Contains($"--- page {p} of {pages} ---", text);

        var pageOne = pages > 1 ? text.Substring(0, text.IndexOf("--- page 2 of", StringComparison.Ordinal)) : text;
        var missing = words.Where(w => pageOne.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0).ToList();
        var need = (int)Math.Ceiling(words.Count * 0.75);
        Assert.True(words.Count - missing.Count >= need,
            "OCR page 1 missed " + string.Join(", ", missing) + " of " + string.Join(", ", words) + "\n" + pageOne);
    }

    [Fact]
    public void Second_run_keeps_the_first_runs_text_file()
    {
        Assert.True(PdfOcrToolProvider.InstalledLanguages().Count > 0, NoLanguage);
        var input = WinToolSupport.Fixture("Q3_Report.pdf");
        var outDir = WinToolSupport.NewDir();
        var first = WinToolSupport.Cli("--tool", "pdf-ocr", "--in", input, "--out", outDir);
        var second = WinToolSupport.Cli("--tool", "pdf-ocr", "--in", input, "--out", outDir);
        Assert.True(first.Code == 0, first.Err);
        Assert.True(second.Code == 0, second.Err);
        Assert.Equal(2, IoDirectory.GetFiles(outDir, "*.txt").Length);
    }

    [Fact]
    public void A_language_that_is_not_installed_fails_and_names_the_option()
    {
        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "none");
        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "pdf-ocr", "--in", WinToolSupport.Fixture("sample.pdf"), "--out", outDir, "--opt", "language=tlh");
        Assert.NotEqual(0, code);
        Assert.Contains("language", stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public void A_file_that_is_not_a_pdf_fails_and_leaves_nothing()
    {
        Assert.True(PdfOcrToolProvider.InstalledLanguages().Count > 0, NoLanguage);
        var dir = WinToolSupport.NewDir();
        var bad = IoPath.Combine(dir, "bad.pdf");
        IoFile.WriteAllText(bad, "this is not a pdf");
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "pdf-ocr", "--in", bad, "--out", outDir);
        Assert.NotEqual(0, code);
        Assert.Contains("cannot open", stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }
}
#endif
